using Elysian.Application.Features.Placements.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// The reserved placements container: fixed names, overwritten on every publish
    /// </summary>
    public interface IPlacementStore
    {
        /// <summary>
        /// Public (CDN) URL for a path in the container; these exact URLs are what gets purged
        /// </summary>
        string GetPublicUrl(string path);

        /// <summary>
        /// Null when nothing has been published at this path yet
        /// </summary>
        Task<PublishedPlacement?> GetPublishedAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Overwrites the file as WebP with the placement Cache-Control. Returns the new ETag.
        /// </summary>
        Task<string> UploadAsync(string path, byte[] content, PlacementMetadata metadata, CancellationToken cancellationToken = default);

        /// <summary>
        /// Null when another publish holds the lock
        /// </summary>
        Task<IPlacementLock?> TryAcquireLockAsync(string path, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A short lease; renew it between long steps. Disposing releases it.
    /// </summary>
    public interface IPlacementLock : IAsyncDisposable
    {
        Task RenewAsync(CancellationToken cancellationToken = default);
    }
}
