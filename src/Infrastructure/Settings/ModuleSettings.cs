namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "Modules" configuration section
    /// </summary>
    public class ModuleSettings
    {
        public const string SectionName = "Modules";

        /// <summary>
        /// When false every tenant may use every module, which is how hosts that serve a single site behave.
        /// The shared host turns this on.
        /// </summary>
        public bool Enforce { get; set; }

        /// <summary>
        /// How long a tenant's modules are served from memory before re-reading the database
        /// </summary>
        public int CacheSeconds { get; set; } = 60;
    }
}
