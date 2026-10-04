using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Serves the cached access token while it has a few minutes left, otherwise refreshes it and stores the result
    /// (including a new refresh token when Google rotates it). An invalid_grant is logged as an error with the fix.
    /// </summary>
    public class GoogleAccessTokenProvider(IGoogleTokenStore tokenStore, IGoogleOAuthClient oauthClient, TimeProvider timeProvider,
        ILogger<GoogleAccessTokenProvider> logger) : IGoogleAccessTokenProvider
    {
        /// <summary>
        /// Refresh a little early so a token never expires mid-request
        /// </summary>
        private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

        public async Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var token = await tokenStore.GetAsync(cancellationToken)
                ?? throw new CalendarAuthorizationException(CalendarAuthorizationFailure.NotConnected,
                    "Google Calendar isn't connected. Paste a refresh token on the admin Calendar page (/admin/calendar).");

            var now = timeProvider.GetUtcNow();
            if (!forceRefresh && token.AccessToken != null && token.AccessTokenExpiresUtc > now + ExpiryMargin)
            {
                return token.AccessToken;
            }

            RefreshedGoogleToken refreshed;
            try
            {
                refreshed = await oauthClient.RefreshAsync(token.RefreshToken, cancellationToken);
            }
            catch (CalendarAuthorizationException ex) when (ex.Failure == CalendarAuthorizationFailure.Revoked)
            {
                logger.LogError(CalendarAuthorizationException.ReconnectHelp);
                throw;
            }

            if (refreshed.RefreshToken != null)
            {
                logger.LogInformation("Google issued a new refresh token for {AccountId}; the stored token was replaced", token.AccountId);
            }

            await tokenStore.SaveAsync(token with
            {
                RefreshToken = refreshed.RefreshToken ?? token.RefreshToken,
                ConnectedUtc = refreshed.RefreshToken != null ? now : token.ConnectedUtc,
                Scope = refreshed.Scope ?? token.Scope,
                AccessToken = refreshed.AccessToken,
                AccessTokenExpiresUtc = now + refreshed.ExpiresIn,
            }, cancellationToken);

            return refreshed.AccessToken;
        }
    }
}
