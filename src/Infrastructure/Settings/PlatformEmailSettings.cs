namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "PlatformEmail" configuration section: how email is sent for sites that have no sending
    /// domain of their own, and how much any one site may send.
    /// </summary>
    public class PlatformEmailSettings
    {
        public const string SectionName = "PlatformEmail";

        /// <summary>
        /// A domain verified with the email provider, e.g. "send.example.com". A site drawn with a shared
        /// template sends as "{site name}" &lt;{identifier}@{this}&gt;. Empty, such a site falls back to the
        /// host's own sender.
        /// </summary>
        public string? SendingDomain { get; set; }

        /// <summary>
        /// The most emails one site may send in a day (UTC). 0 means no limit.
        /// </summary>
        public int DailyLimitPerTenant { get; set; } = 200;

        /// <summary>
        /// How long a contact email code works, in minutes
        /// </summary>
        public int CodeLifetimeMinutes { get; set; } = 30;
    }
}
