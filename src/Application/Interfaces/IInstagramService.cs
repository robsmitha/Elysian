using Elysian.Application.Features.Instagram.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Instagram API with Instagram Login (graph.instagram.com). Calls use the token from
    /// <see cref="IInstagramTokenStore"/> and throw <see cref="Exceptions.InstagramApiException"/> on failure.
    /// </summary>
    public interface IInstagramService
    {
        /// <summary>
        /// Most recent posts, newest first, skipping any without a usable image
        /// </summary>
        Task<List<InstagramPostModel>> GetRecentPostsAsync(int count, CancellationToken cancellationToken = default);

        /// <summary>
        /// Exchanges the stored token for a fresh 60-day token and saves it to the store.
        /// Instagram rejects tokens less than 24 hours old; callers decide when a refresh is due.
        /// </summary>
        Task<InstagramToken> RefreshTokenAsync(CancellationToken cancellationToken = default);
    }
}
