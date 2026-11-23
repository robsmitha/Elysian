using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Stands in until a host is given a real edge store. Editing and saving work without one; publishing says
    /// plainly why it can't.
    /// </summary>
    public class UnconfiguredSiteContentEdge : ISiteContentEdge
    {
        public const string Message = "This host has no edge store configured, so site content can't be published yet.";

        public Task WriteVersionAsync(string tenantIdentifier, int version, string templateKey, int templateVersion, string json,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public Task PointToVersionAsync(string tenantIdentifier, int version, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);
    }

    /// <summary>
    /// Site preview tokens signed with HMAC-SHA256, using the same key as story previews but a purpose of their
    /// own, so neither kind of token can be passed off as the other. A token is "{expiry}.{signature}", both
    /// base64url; the signature also covers the tenant. Nothing is stored, so a token can't be withdrawn
    /// before it expires.
    /// </summary>
    public class HmacSitePreviewTokens(IOptions<StoryPreviewSettings> options, TimeProvider timeProvider) : ISitePreviewTokens
    {
        private const int MinimumKeyBytes = 32;
        private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("elysian.site-preview.v1\n");

        public (string Token, DateTimeOffset ExpiresAt) Issue(string tenantIdentifier)
        {
            var settings = options.Value;
            var key = Key(settings);

            // Whole seconds, because that is all the token carries
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
                timeProvider.GetUtcNow().AddMinutes(Math.Max(1, settings.LifetimeMinutes)).ToUnixTimeSeconds());

            var payload = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(payload, expiresAt.ToUnixTimeSeconds());

            return ($"{Base64UrlEncoder.Encode(payload)}.{Base64UrlEncoder.Encode(Sign(key, tenantIdentifier, payload))}", expiresAt);
        }

        public bool IsValid(string tenantIdentifier, string? token)
        {
            var key = Key(options.Value);

            var parts = token?.Split('.');
            if (parts is not { Length: 2 } || !TryDecode(parts[0], out var payload) || !TryDecode(parts[1], out var signature)
                || payload.Length != sizeof(long))
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(signature, Sign(key, tenantIdentifier, payload))
                && timeProvider.GetUtcNow().ToUnixTimeSeconds() < BinaryPrimitives.ReadInt64BigEndian(payload);
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
                throw new InvalidOperationException($"{name} is not configured, so site previews can't be linked to.");
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
