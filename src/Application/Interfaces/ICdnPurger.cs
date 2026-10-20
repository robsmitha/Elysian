using Elysian.Application.Features.Placements.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Removes overwritten files from the CDN edge so the next request fetches the new bytes
    /// </summary>
    public interface ICdnPurger
    {
        /// <summary>
        /// False locally (no zone/token): purging is skipped
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Purges these exact URLs. Throws when the CDN refuses or keeps failing.
        /// </summary>
        Task PurgeAsync(IReadOnlyCollection<string> urls, CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests each file through the CDN and returns the ones still served with a different ETag
        /// </summary>
        Task<IReadOnlyList<PublishedFile>> FindStaleAsync(IReadOnlyList<PublishedFile> files, CancellationToken cancellationToken = default);
    }
}
