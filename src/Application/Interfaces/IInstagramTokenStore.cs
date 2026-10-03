namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Holds the current Instagram access token, which changes every time it's refreshed. Hosts pick the
    /// implementation: the file store works for a single local or always-on instance, while a shared store
    /// (e.g. Azure Blob Storage) is needed once more than one instance can refresh or read the token.
    /// </summary>
    public interface IInstagramTokenStore
    {
        /// <summary>
        /// The current token, or null when none is stored or configured
        /// </summary>
        Task<InstagramToken?> GetAsync(CancellationToken cancellationToken = default);

        Task SaveAsync(InstagramToken token, CancellationToken cancellationToken = default);
    }

    /// <param name="AccessToken">Secret: never log it or return it from an endpoint</param>
    /// <param name="LastRefreshedUtc">When the token was issued or last refreshed (or first seeded, if unknown)</param>
    public record InstagramToken(string AccessToken, DateTimeOffset LastRefreshedUtc)
    {
        // Keeps the token out of anything that logs or formats this record
        public override string ToString() => $"{nameof(InstagramToken)} {{ LastRefreshedUtc = {LastRefreshedUtc:O} }}";
    }
}
