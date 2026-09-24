namespace Elysian.Application.Interfaces
{
    public interface IPhotoStorage
    {
        /// <summary>
        /// Short-lived, create/write-only SAS URL for a single original blob
        /// </summary>
        Task<Uri> CreateOriginalUploadUriAsync(string blobName, CancellationToken cancellationToken = default);
        Task<Stream> OpenOriginalAsync(string blobName, CancellationToken cancellationToken = default);
        Task DeleteOriginalAsync(string blobName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads an optimized variant with long-lived immutable cache headers
        /// </summary>
        Task UploadVariantAsync(string blobName, byte[] content, string contentType, CancellationToken cancellationToken = default);
        Task DeleteVariantsAsync(string prefix, CancellationToken cancellationToken = default);

        /// <summary>
        /// Public (CDN) URL for a path inside the variants container
        /// </summary>
        string GetPublicUrl(string blobPath);
    }
}
