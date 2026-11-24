namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// How much a site drawn with a shared template may upload. Section "PlatformStorage".
    /// </summary>
    public class PlatformStorageSettings
    {
        public const string SectionName = "PlatformStorage";

        /// <summary>
        /// The uploaded photos a site may hold, in gigabytes, unless the site has a limit of its own
        /// (TenantInfo.StorageQuotaGb). 0 or less means no limit.
        /// </summary>
        public int DefaultQuotaGb { get; set; } = 25;
    }
}
