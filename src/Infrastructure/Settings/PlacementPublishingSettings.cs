namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Reserved placement images: fixed URLs in a public container, overwritten and purged from the CDN when an admin
    /// changes what a slot shows. Section "PlacementPublishing".
    /// </summary>
    public class PlacementPublishingSettings
    {
        public const string SectionName = "PlacementPublishing";

        /// <summary>
        /// Off: enqueueing does nothing and the publish command returns Disabled. The rollback switch.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// e.g. https://account.blob.core.windows.net/placements, written with the app's managed identity
        /// (Storage Blob Data Contributor on this container only)
        /// </summary>
        public string? BlobContainerUri { get; set; }

        /// <summary>
        /// Local development only (Azurite): used instead of the managed identity when set
        /// </summary>
        public string? ConnectionString { get; set; }

        public string ContainerName { get; set; } = "placements";

        /// <summary>
        /// Public URL of the container, e.g. https://images.example.com/placements. These exact URLs are purged.
        /// </summary>
        public string PublicBaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Not immutable: these names are overwritten. A CDN rule should set a long edge TTL and pin this browser TTL.
        /// </summary>
        public string CacheControl { get; set; } = "public, max-age=300";

        /// <summary>
        /// Second purge after this delay, catching a cache miss that fetched old bytes just before they were
        /// overwritten and stored them after the first purge
        /// </summary>
        public int RepurgeDelaySeconds { get; set; } = 60;

        /// <summary>
        /// After purging, request each URL through the CDN and check it serves the new ETag
        /// </summary>
        public bool ConfirmAfterPurge { get; set; } = true;
    }

    /// <summary>
    /// Section "Cloudflare". Purging is skipped when either value is missing (local development).
    /// </summary>
    public class CloudflareSettings
    {
        public const string SectionName = "Cloudflare";

        public string? ZoneId { get; set; }

        /// <summary>
        /// API token with only Zone → Cache Purge → Purge on the site's zone. Keep it in Key Vault.
        /// </summary>
        public string? ApiToken { get; set; }

        public string ApiBaseUrl { get; set; } = "https://api.cloudflare.com/client/v4/";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ZoneId) && !string.IsNullOrWhiteSpace(ApiToken);
    }
}
