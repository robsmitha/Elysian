namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "Edge" configuration section
    /// </summary>
    public class EdgeSettings
    {
        public const string SectionName = "Edge";

        /// <summary>
        /// How long an edge client is served from memory before re-reading the database
        /// </summary>
        public int CacheSeconds { get; set; } = 60;

        /// <summary>
        /// A key that doesn't match a cached client is checked against the database again, in case the key was
        /// just rotated, but no more often than this. Keeps wrong keys from turning into a query each.
        /// </summary>
        public int RecheckAfterSeconds { get; set; } = 5;
    }
}
