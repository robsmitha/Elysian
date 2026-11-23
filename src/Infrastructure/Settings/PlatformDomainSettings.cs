using Elysian.Domain.Platforms;

namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// The domain each platform's sites get their first address under. Section "PlatformDomains", e.g.
    /// PlatformDomains:Domains:photography = example-platform.com gives the site "friend" the address
    /// friend.example-platform.com the moment it is created. The domain's wildcard DNS record and the route
    /// to the Worker that serves the platform's sites are set up once, outside this.
    /// </summary>
    public class PlatformDomainSettings
    {
        public const string SectionName = "PlatformDomains";

        /// <summary>
        /// By platform key. A platform left out gives its sites no address; one is added by hand.
        /// </summary>
        public Dictionary<string, string> Domains { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A tenant's address under its platform's domain, or null when the platform has none configured or
        /// what is configured isn't a domain
        /// </summary>
        public string? AddressFor(string platformKey, string tenantIdentifier)
        {
            var domain = Domains.FirstOrDefault(d => string.Equals(d.Key, platformKey, StringComparison.OrdinalIgnoreCase)).Value;
            var host = TenantHosts.Normalize($"{tenantIdentifier}.{TenantHosts.Normalize(domain)}");
            return !string.IsNullOrWhiteSpace(domain) && TenantHosts.IsWellFormed(host) && TenantHosts.IsSubdomainOf(host, tenantIdentifier)
                ? host
                : null;
        }
    }
}
