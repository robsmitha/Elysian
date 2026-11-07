using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Responses.Plaid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Checks the Plaid-Verification header on a webhook (Plaid's documented scheme): an ES256 JWT signed by one of
    /// Plaid's keys, issued within the last 5 minutes, whose request_body_sha256 claim matches the raw body.
    /// Keys are fetched by key id from /webhook_verification_key/get and cached.
    /// </summary>
    public class PlaidWebhookVerifier(IFinancialService plaid, TimeProvider timeProvider, ILogger<PlaidWebhookVerifier> logger)
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
        private static readonly ConcurrentDictionary<string, WebhookVerificationKey> Keys = new();

        public async Task<bool> VerifyAsync(string? jwt, byte[] body)
        {
            if (string.IsNullOrWhiteSpace(jwt))
            {
                logger.LogWarning("Webhook rejected: no Plaid-Verification header");
                return false;
            }

            var parts = jwt.Split('.');
            if (parts.Length != 3)
            {
                return Reject("malformed token");
            }

            JObject header, claims;
            byte[] signature;
            try
            {
                header = JObject.Parse(Encoding.UTF8.GetString(Base64Url(parts[0])));
                claims = JObject.Parse(Encoding.UTF8.GetString(Base64Url(parts[1])));
                signature = Base64Url(parts[2]);
            }
            catch (Exception ex) when (ex is FormatException or Newtonsoft.Json.JsonException)
            {
                return Reject("unreadable token");
            }

            if (header["alg"]?.Value<string>() != "ES256")
            {
                return Reject("algorithm is not ES256");
            }
            var keyId = header["kid"]?.Value<string>();
            if (string.IsNullOrEmpty(keyId))
            {
                return Reject("no key id");
            }

            var key = await GetKeyAsync(keyId);
            if (key == null || key.expired_at != null)
            {
                return Reject("unknown or expired key");
            }

            using (var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = Base64Url(key.x), Y = Base64Url(key.y) }
            }))
            {
                // JWS signs "header.payload"; ES256 signatures are r||s, .NET's default format
                var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
                if (!ecdsa.VerifyData(signed, signature, HashAlgorithmName.SHA256))
                {
                    return Reject("bad signature");
                }
            }

            var issuedAt = claims["iat"]?.Value<long?>();
            if (issuedAt == null || timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(issuedAt.Value) > MaxAge)
            {
                return Reject("token too old");
            }

            var claimedHash = claims["request_body_sha256"]?.Value<string>() ?? "";
            var actualHash = Sha256Hex(body);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(claimedHash.ToLowerInvariant()), Encoding.ASCII.GetBytes(actualHash)))
            {
                return Reject("body hash mismatch");
            }

            return true;
        }

        public static string Sha256Hex(byte[] body) => Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

        private async Task<WebhookVerificationKey?> GetKeyAsync(string keyId)
        {
            if (Keys.TryGetValue(keyId, out var cached) && cached.expired_at == null)
            {
                return cached;
            }
            try
            {
                var key = await plaid.GetWebhookVerificationKeyAsync(keyId);
                if (key != null)
                {
                    Keys[keyId] = key;
                }
                return key;
            }
            catch (FinancialServiceException ex)
            {
                logger.LogWarning("Couldn't fetch Plaid webhook key {KeyId}: {Code}", keyId, ex.Error?.error_code);
                return null;
            }
        }

        private bool Reject(string reason)
        {
            logger.LogWarning("Webhook rejected: {Reason}", reason);
            return false;
        }

        private static byte[] Base64Url(string value)
        {
            var s = value.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            return Convert.FromBase64String(s);
        }

        /// <summary>For tests: forget cached keys.</summary>
        internal static void ClearKeyCache() => Keys.Clear();
    }
}
