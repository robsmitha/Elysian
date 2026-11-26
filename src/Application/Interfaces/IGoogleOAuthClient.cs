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

        /// <summary>
        /// True when the client and the address Google returns to are both configured, so a photographer can connect
        /// </summary>
        bool CanAuthorize { get; }

        /// <summary>
        /// The Google page where the photographer picks their account and allows access. Google returns to
        /// GoogleCalendar:RedirectUri with a code and the same <paramref name="state"/>.
        /// </summary>
        /// <param name="loginHint">The account connected before, to preselect it</param>
        string AuthorizationUrl(string state, string? loginHint = null);

        /// <summary>
        /// Exchanges the code Google returned for tokens. Throws <see cref="Exceptions.CalendarAuthorizationException"/>
        /// with <see cref="Exceptions.CalendarAuthorizationFailure.Revoked"/> when the code is used, expired or not ours.
        /// </summary>
        Task<GoogleAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tells Google to withdraw the grant behind this token. Throws when Google refuses or can't be reached.
        /// </summary>
        Task RevokeAsync(string token, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// What Google hands back when a photographer allows access
    /// </summary>
    /// <param name="RefreshToken">Null when Google gave no lasting access</param>
    /// <param name="Scope">The scopes actually granted, space separated: the person can untick any of them</param>
    /// <param name="Email">The Google account's address, when it was shared</param>
    public record GoogleAuthorization(string AccessToken, TimeSpan ExpiresIn, string? RefreshToken, string? Scope, string? Email)
    {
        public override string ToString() => $"{nameof(GoogleAuthorization)} {{ Email = {Email}, Lasting = {RefreshToken != null}, Scope = {Scope} }}";
    }

    /// <param name="RefreshToken">Set only when Google issued a different refresh token, which replaces the stored one</param>
    public record RefreshedGoogleToken(string AccessToken, TimeSpan ExpiresIn, string? RefreshToken, string? Scope)
    {
        public override string ToString() => $"{nameof(RefreshedGoogleToken)} {{ ExpiresIn = {ExpiresIn}, Rotated = {RefreshToken != null}, Scope = {Scope} }}";
    }
}
