namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "StoryPreview" configuration section
    /// </summary>
    public class StoryPreviewSettings
    {
        public const string SectionName = "StoryPreview";

        /// <summary>
        /// The secret preview links are signed with: at least 32 random bytes, base64 encoded. Changing it ends
        /// every link issued before. Keep it in a secret store, never in committed config.
        /// </summary>
        public string? SigningKey { get; set; }

        /// <summary>
        /// How long a preview link works
        /// </summary>
        public int LifetimeMinutes { get; set; } = 60;
    }
}
