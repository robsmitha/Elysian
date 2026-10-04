namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "Instagram" configuration section (Instagram API with Instagram Login, graph.instagram.com).
    /// The access token itself is per tenant and lives in the OAuthToken table, set through ConnectInstagramCommand.
    /// </summary>
    public class InstagramSettings
    {
        public const string SectionName = "Instagram";

        /// <summary>
        /// Number of recent posts mirrored from Instagram
        /// </summary>
        public int PostCount { get; set; } = 12;

        /// <summary>
        /// Thumbnail widths generated for each post. Feed tiles are small, so these stay well under the portfolio's sizes.
        /// </summary>
        public int[] ThumbnailWidths { get; set; } = [320, 640];

        /// <summary>
        /// How long the post list is served from memory before re-reading the database
        /// </summary>
        public int CacheMinutes { get; set; } = 5;

        /// <summary>
        /// Minimum age of the token before it's refreshed. Instagram rejects refreshing a token
        /// less than 24 hours old, so values under 1 day are treated as 1 day.
        /// </summary>
        public int RefreshIntervalDays { get; set; } = 7;
    }
}
