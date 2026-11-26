using System.Text;
using System.Text.Json;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.GoogleCalendar;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Google's sign-in and token endpoints, through Google.Apis.Auth. No data store is attached to the flow: tokens
    /// are persisted by the caller through <see cref="IGoogleTokenStore"/>.
    /// </summary>
    public class GoogleOAuthClient(IOptions<GoogleCalendarSettings> options) : IGoogleOAuthClient
    {
        /// <summary>
        /// Google omits expires_in only in unusual cases; access tokens last an hour
        /// </summary>
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

        private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

        /// <summary>
        /// The flow's key for a token. Nothing is stored under it, since no data store is attached.
        /// </summary>
        private const string FlowUser = "calendar";

        public bool CanAuthorize =>
            !string.IsNullOrWhiteSpace(options.Value.ClientId)
            && !string.IsNullOrWhiteSpace(options.Value.ClientSecret)
            && !string.IsNullOrWhiteSpace(options.Value.RedirectUri);

        public string AuthorizationUrl(string state, string? loginHint = null)
        {
            var settings = options.Value;
            var parameters = new List<KeyValuePair<string, string>>
            {
                new("client_id", settings.ClientId),
                new("redirect_uri", settings.RedirectUri),
                new("response_type", "code"),
                new("scope", string.Join(' ', GoogleCalendarScopes.Requested)),
                // Lasting access, so booking works while the photographer is away
                new("access_type", "offline"),
                // "consent" makes Google issue a refresh token every time, not only the first; "select_account"
                // lets them pick the account that holds the calendar, which may not be the one they're signed in to
                new("prompt", "consent select_account"),
                new("state", state),
            };
            if (!string.IsNullOrWhiteSpace(loginHint))
            {
                parameters.Add(new("login_hint", loginHint));
            }

            return AuthorizationEndpoint + "?" + string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        }

        public async Task<GoogleAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            using var flow = CreateFlow();

            TokenResponse response;
            try
            {
                response = await flow.ExchangeCodeForTokenAsync(FlowUser, code, options.Value.RedirectUri, cancellationToken);
            }
            catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
            {
                throw new CalendarAuthorizationException(CalendarAuthorizationFailure.Revoked,
                    "Google rejected the sign-in code: it was already used, has expired, or was issued for another address.", ex);
            }
            catch (TokenResponseException ex) when (ex.Error?.Error is "invalid_client" or "unauthorized_client" or "redirect_uri_mismatch")
            {
                throw new CalendarAuthorizationException(CalendarAuthorizationFailure.NotConfigured,
                    $"Google rejected the OAuth client ({ex.Error.Error}). Check {GoogleCalendarSettings.SectionName}:ClientId, ClientSecret and RedirectUri.", ex);
            }

            return new GoogleAuthorization(response.AccessToken, Lifetime(response),
                string.IsNullOrEmpty(response.RefreshToken) ? null : response.RefreshToken, response.Scope, EmailFrom(response.IdToken));
        }

        public async Task RevokeAsync(string token, CancellationToken cancellationToken = default)
        {
            using var flow = CreateFlow();
            await flow.RevokeTokenAsync(FlowUser, token, cancellationToken);
        }

        /// <summary>
        /// The address in an ID token. The signature isn't checked: the token is only ever read straight from Google's
        /// token endpoint, over TLS, in answer to our own request, and the address is used for display.
        /// </summary>
        public static string? EmailFrom(string? idToken)
        {
            var parts = idToken?.Split('.');
            if (parts?.Length != 3)
            {
                return null;
            }

            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                return document.RootElement.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String
                    ? email.GetString()
                    : null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return null;
            }
        }

        private GoogleAuthorizationCodeFlow CreateFlow()
        {
            var settings = options.Value;
            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                throw new CalendarAuthorizationException(CalendarAuthorizationFailure.NotConfigured,
                    $"{GoogleCalendarSettings.SectionName}:ClientId and ClientSecret must be configured to use Google Calendar.");
            }

            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = settings.ClientId, ClientSecret = settings.ClientSecret },
                Scopes = GoogleCalendarScopes.Requested,
            });
        }

        private static TimeSpan Lifetime(TokenResponse response) =>
            response.ExpiresInSeconds is long seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultLifetime;

        public async Task<RefreshedGoogleToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            using var flow = CreateFlow();

            TokenResponse response;
            try
            {
                response = await flow.RefreshTokenAsync(FlowUser, refreshToken, cancellationToken);
            }
            catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
            {
                throw new CalendarAuthorizationException(CalendarAuthorizationFailure.Revoked, CalendarAuthorizationException.ReconnectHelp, ex);
            }
            catch (TokenResponseException ex) when (ex.Error?.Error is "invalid_client" or "unauthorized_client")
            {
                throw new CalendarAuthorizationException(CalendarAuthorizationFailure.NotConfigured,
                    $"Google rejected the OAuth client ({ex.Error.Error}). Check {GoogleCalendarSettings.SectionName}:ClientId and ClientSecret.", ex);
            }

            // The flow copies the old refresh token in when Google doesn't send one, so only a different value is a rotation
            var rotated = !string.IsNullOrEmpty(response.RefreshToken) && response.RefreshToken != refreshToken
                ? response.RefreshToken
                : null;

            return new RefreshedGoogleToken(response.AccessToken, Lifetime(response), rotated, response.Scope);
        }
    }
}
