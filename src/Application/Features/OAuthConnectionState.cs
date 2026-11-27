using System.Security.Cryptography;
using System.Text;

namespace Elysian.Application.Features
{
    /// <summary>
    /// The "state" value that travels to a sign-in provider and back when a tenant connects an account. It names the
    /// site, so the one return page shared by every tenant knows whose connection this is, and ends in a random value
    /// that is stored against the person who started and accepted once, within <see cref="Lifetime"/>.
    /// </summary>
    public static class OAuthConnectionState
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        public static string Create(string siteIdentifier) =>
            $"{siteIdentifier}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";

        public static bool Matches(string stored, string returned) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(returned));
    }
}
