using System.Security.Cryptography;
using System.Text;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Encrypts Plaid access tokens with AES-GCM. Stored form: "v1:{keyId}:{base64(nonce | ciphertext | tag)}".
    /// The key id lets old tokens be read after the key is rotated (PreviousTokenEncryptionKeys).
    /// </summary>
    public class AccessTokenProtector(IOptions<PlaidSettings> options) : IAccessTokenProtector
    {
        private const string Version = "v1";
        private const int NonceSize = 12;
        private const int TagSize = 16;

        private readonly PlaidSettings settings = options.Value;

        public bool CanProtect => !string.IsNullOrWhiteSpace(settings.TokenEncryptionKey);

        public string Protect(string plaintext)
        {
            var key = Key(settings.TokenEncryptionKey)
                ?? throw new InvalidOperationException("PlaidSettings:TokenEncryptionKey is not configured, so access tokens can't be stored.");

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var data = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[data.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, data, ciphertext, tag, AssociatedData(settings.TokenEncryptionKeyId));
            }

            var payload = new byte[NonceSize + ciphertext.Length + TagSize];
            nonce.CopyTo(payload, 0);
            ciphertext.CopyTo(payload, NonceSize);
            tag.CopyTo(payload, NonceSize + ciphertext.Length);
            return $"{Version}:{settings.TokenEncryptionKeyId}:{Convert.ToBase64String(payload)}";
        }

        public string Unprotect(string protectedValue)
        {
            var parts = protectedValue.Split(':', 3);
            if (parts.Length != 3 || parts[0] != Version)
            {
                throw new CryptographicException("Unrecognized access token format.");
            }

            var keyId = parts[1];
            var keyText = keyId == settings.TokenEncryptionKeyId
                ? settings.TokenEncryptionKey
                : settings.PreviousTokenEncryptionKeys.GetValueOrDefault(keyId);
            var key = Key(keyText) ?? throw new CryptographicException($"No encryption key is configured for key id '{keyId}'.");

            var payload = Convert.FromBase64String(parts[2]);
            if (payload.Length < NonceSize + TagSize)
            {
                throw new CryptographicException("Access token ciphertext is too short.");
            }

            var nonce = payload.AsSpan(0, NonceSize);
            var ciphertext = payload.AsSpan(NonceSize, payload.Length - NonceSize - TagSize);
            var tag = payload.AsSpan(payload.Length - TagSize);
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(keyId));
            }
            return Encoding.UTF8.GetString(plaintext);
        }

        // Binds the ciphertext to its key id, so a token can't be relabeled to another key
        private static byte[] AssociatedData(string keyId) => Encoding.UTF8.GetBytes($"plaid-access-token:{keyId}");

        private static byte[]? Key(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return null;
            }
            var key = Convert.FromBase64String(base64);
            return key.Length == 32 ? key : throw new CryptographicException("The token encryption key must be 32 bytes (base64).");
        }
    }
}
