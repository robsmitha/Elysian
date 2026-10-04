namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Holds the current tenant's Instagram access token, which changes every time it's refreshed
    /// </summary>
    public interface IInstagramTokenStore
    {
        /// <summary>
        /// The current tenant's token, or null when no Instagram account is connected
        /// </summary>
        Task<InstagramToken?> GetAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores the token as the current tenant's only Instagram connection, replacing any other account
        /// </summary>
        Task SaveAsync(InstagramToken token, CancellationToken cancellationToken = default);
    }

    /// <param name="AccessToken">Secret: never log it or return it from an endpoint</param>
    /// <param name="UserId">Instagram account id (user_id)</param>
    /// <param name="LastRefreshedUtc">When the token was connected or last refreshed</param>
    /// <param name="ExpiresUtc">Known once Instagram has refreshed the token; null for a pasted token of unknown age</param>
    public record InstagramToken(string AccessToken, string UserId, DateTimeOffset LastRefreshedUtc, DateTimeOffset? ExpiresUtc)
    {
        // Keeps the token out of anything that logs or formats this record
        public override string ToString() =>
            $"{nameof(InstagramToken)} {{ UserId = {UserId}, LastRefreshedUtc = {LastRefreshedUtc:O}, ExpiresUtc = {ExpiresUtc:O} }}";
    }
}
