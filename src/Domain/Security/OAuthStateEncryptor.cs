using System.Security.Cryptography;
using System.Text;

namespace Elysian.Domain.Security
{
    public static class OAuthStateEncryptor
    {
        public static string ComputeHash(string secretKey, string input)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var inputBytes = Encoding.UTF8.GetBytes(input);

            using HMACSHA256 hmac = new(keyBytes);
            var hashBytes = hmac.ComputeHash(inputBytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public static bool Validate(string secretKey, string input, string hash)
        {
            var computed = ComputeHash(secretKey, input);
            return hash.Equals(computed, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
