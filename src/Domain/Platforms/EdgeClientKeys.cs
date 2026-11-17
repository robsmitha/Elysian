using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Elysian.Domain.Platforms
{
    /// <summary>
    /// An edge credential is "{client name}.{key}". The name finds the client; the key proves it.
    /// </summary>
    public static partial class EdgeClientKeys
    {
        public const string HeaderName = "X-Edge-Auth";
        public const string TenantHeaderName = "X-Tenant";

        [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,62}[a-z0-9])$")]
        private static partial Regex NamePattern();

        public static bool IsValidName(string? name) => name != null && NamePattern().IsMatch(name);

        /// <summary>
        /// 256 random bits, so a plain SHA-256 is enough to store it safely
        /// </summary>
        public static string GenerateKey() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        public static string Format(string name, string key) => $"{name}.{key}";

        public static bool TryParse(string? credential, out string name, out string key)
        {
            name = key = string.Empty;
            if (string.IsNullOrWhiteSpace(credential))
            {
                return false;
            }

            var dot = credential.IndexOf('.');
            if (dot <= 0 || dot == credential.Length - 1)
            {
                return false;
            }

            name = credential[..dot];
            key = credential[(dot + 1)..];
            return IsValidName(name);
        }

        /// <summary>
        /// Compares in constant time so a wrong key reveals nothing about the right one
        /// </summary>
        public static bool Matches(string key, string? storedHash) =>
            !string.IsNullOrEmpty(storedHash)
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(key)), Encoding.ASCII.GetBytes(storedHash.ToUpperInvariant()));
    }
}
