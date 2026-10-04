using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Refreshes tokens with Google.Apis.Auth. No data store is attached to the flow: tokens are persisted
    /// by the caller through <see cref="IGoogleTokenStore"/>.
    /// </summary>
    public class GoogleOAuthClient(IOptions<GoogleCalendarSettings> options) : IGoogleOAuthClient
    {
        /// <summary>
        /// Google omits expires_in only in unusual cases; access tokens last an hour
        /// </summary>
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

        public async Task<RefreshedGoogleToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            var settings = options.Value;
            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                throw new CalendarAuthorizationException(CalendarAuthorizationFailure.NotConfigured,
                    $"{GoogleCalendarSettings.SectionName}:ClientId and ClientSecret must be configured to use Google Calendar.");
            }

            using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = settings.ClientId, ClientSecret = settings.ClientSecret },
                Scopes = [CalendarService.Scope.Calendar],
            });

            TokenResponse response;
            try
            {
                response = await flow.RefreshTokenAsync("calendar", refreshToken, cancellationToken);
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

            var expiresIn = response.ExpiresInSeconds is long seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultLifetime;
            return new RefreshedGoogleToken(response.AccessToken, expiresIn, rotated, response.Scope);
        }
    }
}
