using System.Text.RegularExpressions;

namespace Elysian.Domain.Platforms
{
    /// <summary>
    /// How a host reaches a tenant's site
    /// </summary>
    public static class TenantDomainKinds
    {
        /// <summary>The tenant's own name under a platform's domain, e.g. friend.example-platform.com</summary>
        public const string Subdomain = "subdomain";

        /// <summary>A domain of the tenant's own, attached to the Worker that serves the site</summary>
        public const string Custom = "custom";

        public static readonly IReadOnlyList<string> All = [Subdomain, Custom];

        public static string? Find(string? kind) =>
            All.FirstOrDefault(k => string.Equals(k, kind?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static class TenantDomainStatuses
    {
        /// <summary>Recorded, but not yet answering: DNS or the certificate is still being set up</summary>
        public const string Pending = "pending";

        /// <summary>Answering for the site</summary>
        public const string Active = "active";

        public static readonly IReadOnlyList<string> All = [Pending, Active];

        public static string? Find(string? status) =>
            All.FirstOrDefault(s => string.Equals(s, status?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A tenant's host is stored once, in one spelling, so a lookup by the host of a request can be an exact
    /// match. It is a bare DNS name: never a URL, never with a port, and never an address the platform uses
    /// for itself.
    /// </summary>
    public static partial class TenantHosts
    {
        public const int MaxLength = 253;

        [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
        private static partial Regex Label();

        /// <summary>
        /// Trimmed, lowercase, without the trailing dot a fully qualified name may carry. Anything that isn't
        /// already a bare host (a scheme, a path, a port) is left in, so <see cref="IsWellFormed"/> rejects it
        /// instead of a different host being stored than the one that was typed.
        /// </summary>
        public static string Normalize(string? host)
        {
            var value = (host ?? string.Empty).Trim().ToLowerInvariant();
            return value.EndsWith('.') ? value[..^1] : value;
        }

        /// <summary>
        /// At least two labels of letters, digits and dashes, each 1 to 63 long and not starting or ending with
        /// a dash, 253 characters in all, with a last label that isn't a number (so an IP address is not a host).
        /// Expects a normalized host.
        /// </summary>
        public static bool IsWellFormed(string? host)
        {
            if (string.IsNullOrEmpty(host) || host.Length > MaxLength)
            {
                return false;
            }

            var labels = host.Split('.');
            return labels.Length >= 2
                && labels.All(label => Label().IsMatch(label))
                && !labels[^1].All(char.IsAsciiDigit);
        }

        /// <summary>
        /// A subdomain host is the tenant's own identifier under a platform's domain, e.g. "friend.example.com"
        /// for the tenant "friend". Holding it to that keeps one tenant from taking another's name, or one the
        /// platform uses itself (identifiers can't be "manage", "images" and so on). Expects a normalized host.
        /// </summary>
        public static bool IsSubdomainOf(string host, string tenantIdentifier)
        {
            var labels = host.Split('.');
            return labels.Length >= 3 && labels[0] == tenantIdentifier;
        }
    }
}
