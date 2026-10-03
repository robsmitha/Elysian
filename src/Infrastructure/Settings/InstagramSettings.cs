namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "Instagram" configuration section (Instagram API with Instagram Login, graph.instagram.com)
    /// </summary>
    public class InstagramSettings
    {
        public const string SectionName = "Instagram";

        /// <summary>
        /// Long-lived Instagram User access token. Only used to seed the <c>IInstagramTokenStore</c>;
        /// refreshed tokens are read from the store, so this value may be older than the one in use.
        /// Regenerate it in the Meta app dashboard if it expires (60 days without a refresh).
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Number of recent posts requested from Instagram
        /// </summary>
        public int PostCount { get; set; } = 12;

        /// <summary>
        /// How long a successful response is served from memory before asking Instagram again
        /// </summary>
        public int CacheMinutes { get; set; } = 30;

        /// <summary>
        /// Minimum age of the token before it's refreshed. Instagram rejects refreshing a token
        /// less than 24 hours old, so values under 1 day are treated as 1 day.
        /// </summary>
        public int RefreshIntervalDays { get; set; } = 7;

        /// <summary>
        /// Where the file token store keeps the current token. Defaults to
        /// %LOCALAPPDATA%/Elysian/instagram-token.json, outside any repository.
        /// </summary>
        public string? TokenFilePath { get; set; }
    }
}
