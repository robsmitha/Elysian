namespace Elysian.Domain.Platforms
{
    public static class PlatformNames
    {
        public const string Photography = "photography";
        public const string Personal = "personal";
    }

    /// <summary>
    /// Which code renders a tenant's public site: the platform's shared template, or a site built for them
    /// </summary>
    public static class RuntimeNames
    {
        public const string Cloud = "cloud";
        public const string Custom = "custom";
    }

    public static class TenantStatusNames
    {
        public const string Trial = "trial";
        public const string Active = "active";
        public const string Suspended = "suspended";
    }

    /// <summary>
    /// A product line that tenants belong to. Included modules are switched on when a tenant is created;
    /// add-on modules can be switched on later. A module in neither list can't be enabled for the platform's tenants.
    /// MerchantType names the kind of merchant record a new tenant gets. DefaultTemplate is the shared
    /// template a new cloud site is drawn with when none is asked for; null for a platform with no template.
    /// </summary>
    public record PlatformDefinition(
        string Key,
        string Name,
        bool HasCloudRuntime,
        IReadOnlyList<string> IncludedModules,
        IReadOnlyList<string> AddOnModules,
        string MerchantType,
        string? DefaultTemplate = null)
    {
        public bool Offers(string module) =>
            IncludedModules.Contains(module, StringComparer.OrdinalIgnoreCase)
            || AddOnModules.Contains(module, StringComparer.OrdinalIgnoreCase);
    }

    public static class PlatformCatalog
    {
        public static readonly PlatformDefinition Photography = new(
            PlatformNames.Photography,
            "Photography",
            HasCloudRuntime: true,
            IncludedModules: [ModuleNames.Photos],
            AddOnModules: [ModuleNames.Booking, ModuleNames.Instagram],
            MerchantType: "Store",
            DefaultTemplate: "classic");

        /// <summary>
        /// Sites built for one owner, with no shared template. Nothing is included; modules are switched on per tenant.
        /// </summary>
        public static readonly PlatformDefinition Personal = new(
            PlatformNames.Personal,
            "Personal",
            HasCloudRuntime: false,
            IncludedModules: [],
            AddOnModules: ModuleNames.All,
            MerchantType: "Portfolio");

        public static readonly IReadOnlyList<PlatformDefinition> All = [Photography, Personal];

        public static PlatformDefinition? Find(string? key) =>
            All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}
