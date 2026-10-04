namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Holds the current tenant's Google Calendar tokens: the long-lived refresh token plus the latest short-lived access token
    /// </summary>
    public interface IGoogleTokenStore
    {
        /// <summary>
        /// The current tenant's tokens, or null when Google Calendar isn't connected
        /// </summary>
        Task<GoogleToken?> GetAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores the tokens as the tenant's only Google connection, replacing any other account
        /// </summary>
        Task SaveAsync(GoogleToken token, CancellationToken cancellationToken = default);
    }

    /// <param name="RefreshToken">Secret: never log it or return it from an endpoint</param>
    /// <param name="AccountId">Calendar id the token was verified against (the account email for "primary")</param>
    /// <param name="AccessToken">Secret: latest access token, cached so every request doesn't refresh</param>
    /// <param name="ConnectedUtc">When the refresh token was stored or last rotated by Google</param>
    /// <param name="LastValidatedUtc">When the token last worked against the Calendar API</param>
    public record GoogleToken(string RefreshToken, string AccountId, string? Scope, string? AccessToken, DateTimeOffset? AccessTokenExpiresUtc,
        DateTimeOffset ConnectedUtc, DateTimeOffset? LastValidatedUtc)
    {
        // Keeps the tokens out of anything that logs or formats this record
        public override string ToString() =>
            $"{nameof(GoogleToken)} {{ AccountId = {AccountId}, ConnectedUtc = {ConnectedUtc:O}, LastValidatedUtc = {LastValidatedUtc:O} }}";
    }
}
