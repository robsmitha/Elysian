namespace Elysian.Infrastructure.Settings
{
    public class PhotoStorageSettings
    {
        /// <summary>
        /// Private container holding uploaded originals
        /// </summary>
        public string OriginalsContainer { get; set; } = "photo-originals";

        /// <summary>
        /// Anonymous blob-read container holding the optimized variants
        /// </summary>
        public string PublicContainer { get; set; } = "photos";

        /// <summary>
        /// Base URL the browser uses for variants, e.g. https://images.example.com/photos.
        /// Falls back to the container's blob endpoint when empty. This is the only place
        /// the CDN hostname lives, so switching CDNs is a config change.
        /// </summary>
        public string? PublicBaseUrl { get; set; }

        public int[] VariantWidths { get; set; } = [400, 800, 1200, 1600, 2400];
        public int WebpQuality { get; set; } = 82;
        public int PlaceholderWidth { get; set; } = 24;
        public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;
        public long MaxPixels { get; set; } = 120_000_000;
        public int UploadUrlLifetimeMinutes { get; set; } = 15;
    }
}
