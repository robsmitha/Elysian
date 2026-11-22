using System.Buffers.Binary;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Preview tokens signed with HMAC-SHA256. A token is "{payload}.{signature}", both base64url: the payload
    /// is the story id and the expiry; the signature also covers the tenant, so a token for one tenant's story
    /// is not valid for another's. Nothing is stored, so a token can't be withdrawn before it expires.
    /// </summary>
    public class HmacStoryPreviewTokens(IOptions<StoryPreviewSettings> options, TimeProvider timeProvider) : IStoryPreviewTokens
    {
        private const int MinimumKeyBytes = 32;
        private const int PayloadBytes = 16 + sizeof(long);
        private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("elysian.story-preview.v1\n");

        public (string Token, DateTimeOffset ExpiresAt) Issue(string tenantIdentifier, Guid storyId)
        {
            var settings = options.Value;
            var key = Key(settings);

            // Whole seconds, because that is all the token carries
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
                timeProvider.GetUtcNow().AddMinutes(Math.Max(1, settings.LifetimeMinutes)).ToUnixTimeSeconds());

            var payload = new byte[PayloadBytes];
            storyId.TryWriteBytes(payload);
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(16), expiresAt.ToUnixTimeSeconds());

            var token = $"{Base64UrlEncoder.Encode(payload)}.{Base64UrlEncoder.Encode(Sign(key, tenantIdentifier, payload))}";
            return (token, expiresAt);
        }

        public Guid? Read(string tenantIdentifier, string? token)
        {
            var key = Key(options.Value);

            var parts = token?.Split('.');
            if (parts is not { Length: 2 } || !TryDecode(parts[0], out var payload) || !TryDecode(parts[1], out var signature)
                || payload.Length != PayloadBytes)
            {
                return null;
            }

            if (!CryptographicOperations.FixedTimeEquals(signature, Sign(key, tenantIdentifier, payload)))
            {
                return null;
            }

            var expiresAt = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(16));
            if (timeProvider.GetUtcNow().ToUnixTimeSeconds() >= expiresAt)
            {
                return null;
            }

            return new Guid(payload.AsSpan(0, 16));
        }

        private static byte[] Sign(byte[] key, string tenantIdentifier, byte[] payload)
        {
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
            hmac.AppendData(Purpose);
            hmac.AppendData(Encoding.UTF8.GetBytes(tenantIdentifier));
            // A tenant identifier never contains a line break, so this keeps tenant and payload apart
            hmac.AppendData("\n"u8);
            hmac.AppendData(payload);
            return hmac.GetHashAndReset();
        }

        private static bool TryDecode(string value, out byte[] bytes)
        {
            try
            {
                bytes = Base64UrlEncoder.DecodeBytes(value);
                return true;
            }
            catch (FormatException)
            {
                bytes = [];
                return false;
            }
        }

        private static byte[] Key(StoryPreviewSettings settings)
        {
            var name = $"{StoryPreviewSettings.SectionName}:{nameof(StoryPreviewSettings.SigningKey)}";
            if (string.IsNullOrWhiteSpace(settings.SigningKey))
            {
                throw new InvalidOperationException($"{name} is not configured, so story previews can't be linked to.");
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(settings.SigningKey.Trim());
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"{name} must be base64.");
            }

            return key.Length >= MinimumKeyBytes
                ? key
                : throw new InvalidOperationException($"{name} must be at least {MinimumKeyBytes} bytes.");
        }
    }
}
