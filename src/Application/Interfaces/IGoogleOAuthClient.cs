namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Google's OAuth token endpoint, using GoogleCalendar:ClientId and ClientSecret
    /// </summary>
    public interface IGoogleOAuthClient
    {
        /// <summary>
        /// Exchanges a refresh token for a new access token. Throws <see cref="Exceptions.CalendarAuthorizationException"/>
        /// with <see cref="Exceptions.CalendarAuthorizationFailure.Revoked"/> on invalid_grant.
        /// </summary>
        Task<RefreshedGoogleToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    }

    /// <param name="RefreshToken">Set only when Google issued a different refresh token, which replaces the stored one</param>
    public record RefreshedGoogleToken(string AccessToken, TimeSpan ExpiresIn, string? RefreshToken, string? Scope)
    {
        public override string ToString() => $"{nameof(RefreshedGoogleToken)} {{ ExpiresIn = {ExpiresIn}, Rotated = {RefreshToken != null}, Scope = {Scope} }}";
    }
}
