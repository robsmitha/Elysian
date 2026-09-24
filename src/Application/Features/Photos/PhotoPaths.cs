namespace Elysian.Application.Features.Photos
{
    /// <summary>
    /// Blob naming for photos. Variant paths include the version so a reprocess never
    /// overwrites a URL that browsers/CDNs have cached as immutable.
    /// </summary>
    public static class PhotoPaths
    {
        public static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"];

        public static string Original(string tenantIdentifier, Guid photoId, string fileName) =>
            $"{tenantIdentifier}/{photoId}/original{Path.GetExtension(fileName).ToLowerInvariant()}";

        public static string PhotoFolder(string tenantIdentifier, Guid photoId) =>
            $"{tenantIdentifier}/{photoId}/";

        public static string VersionFolder(string tenantIdentifier, Guid photoId, int version) =>
            $"{tenantIdentifier}/{photoId}/v{version}";

        public static string Variant(string tenantIdentifier, Guid photoId, int version, int width) =>
            $"{VersionFolder(tenantIdentifier, photoId, version)}/{width}.webp";

        /// <summary>
        /// Tenant identifier is the first path segment of any photo blob name
        /// </summary>
        public static string? TenantFromBlobName(string blobName)
        {
            var slash = blobName.IndexOf('/');
            return slash > 0 ? blobName[..slash] : null;
        }
    }
}
