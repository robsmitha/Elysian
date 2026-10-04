namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Instagram API with Instagram Login (graph.instagram.com). Every call takes the token explicitly, so new tokens
    /// can be verified before they're stored. Failures throw <see cref="Exceptions.InstagramApiException"/>.
    /// </summary>
    public interface IInstagramService
    {
        /// <summary>
        /// The account the token belongs to
        /// </summary>
        Task<InstagramAccount> GetAccountAsync(string accessToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Most recent media, newest first, skipping any without a usable image
        /// </summary>
        Task<List<InstagramMedia>> GetRecentMediaAsync(string accessToken, int count, CancellationToken cancellationToken = default);

        /// <summary>
        /// Exchanges a long-lived token for a fresh 60-day one. Instagram rejects tokens less than 24 hours old.
        /// </summary>
        Task<RefreshedInstagramToken> RefreshTokenAsync(string accessToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a media image from Instagram's CDN into a seekable stream
        /// </summary>
        Task<Stream> DownloadImageAsync(string imageUrl, CancellationToken cancellationToken = default);
    }

    public record InstagramAccount(string UserId, string Username);

    /// <param name="ImageUrl">Video thumbnail for VIDEO, otherwise the media itself (first image of an album)</param>
    public record InstagramMedia(string Id, string ImageUrl, string Permalink, string? Caption, string MediaType, DateTimeOffset? Timestamp);

    public record RefreshedInstagramToken(string AccessToken, TimeSpan? ExpiresIn)
    {
        public override string ToString() => $"{nameof(RefreshedInstagramToken)} {{ ExpiresIn = {ExpiresIn} }}";
    }
}
