using System.Text.RegularExpressions;

namespace Elysian.Domain.Platforms
{
    /// <summary>
    /// A tenant's identifier becomes its subdomain, its storage folder and part of its sender address, so it
    /// has to be a valid DNS label and mustn't collide with a name the platform uses itself.
    /// </summary>
    public static partial class TenantIdentifiers
    {
        public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "www", "manage", "admin", "api", "app", "images", "img", "cdn", "static", "assets", "files",
            "send", "mail", "email", "smtp", "mx", "ns", "ftp",
            "login", "signin", "signup", "auth", "account", "accounts", "billing", "pay", "payments",
            "support", "help", "status", "docs", "blog", "about", "contact", "legal", "terms", "privacy",
            "site", "sites", "cloud", "platform", "system", "root", "test", "staging", "dev", "demo",
        };

        [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$")]
        private static partial Regex Pattern();

        public static string Normalize(string? identifier) => (identifier ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>
        /// 3 to 63 lowercase letters, digits and dashes, starting and ending with a letter or digit, with no
        /// doubled dash (which DNS reserves for encoded names)
        /// </summary>
        public static bool IsWellFormed(string? identifier) =>
            identifier != null && Pattern().IsMatch(identifier) && !identifier.Contains("--");

        public static bool IsReserved(string? identifier) => identifier != null && Reserved.Contains(identifier);
    }
}
