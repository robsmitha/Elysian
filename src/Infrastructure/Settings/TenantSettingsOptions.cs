namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Marks a setting only the host may set, such as an API key or a storage address. A tenant's stored
    /// settings can't override it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class PlatformOnlyAttribute : Attribute
    {
    }

    /// <summary>
    /// Bound from the "TenantSettings" configuration section
    /// </summary>
    public class TenantSettingsOptions
    {
        public const string SectionName = "TenantSettings";

        /// <summary>
        /// How long a tenant's settings are served from memory before re-reading the database
        /// </summary>
        public int CacheSeconds { get; set; } = 60;
    }

    /// <summary>
    /// Bound from the "TenantSecrets" configuration section
    /// </summary>
    public class TenantSecretOptions
    {
        public const string SectionName = "TenantSecrets";

        /// <summary>
        /// Key Vault holding per-tenant secrets named tenant-{identifier}-{name}. When empty, secrets are read
        /// from configuration instead, as TenantSecrets:{identifier}:{name}.
        /// </summary>
        public string? VaultUri { get; set; }

        public int CacheSeconds { get; set; } = 300;
    }
}
