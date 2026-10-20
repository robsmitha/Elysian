using System.Globalization;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    public class AzurePlacementStore(PlacementStorageClients clients, IOptions<PlacementPublishingSettings> settings) : IPlacementStore
    {
        private const string FingerprintKey = "fingerprint";
        private const string SourceKey = "source";
        private const string PhotoIdKey = "photoid";
        private const string SourceWidthKey = "sourcewidth";
        private const string PublishedAtKey = "publishedat";

        private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

        private readonly PlacementPublishingSettings _settings = settings.Value;

        public string GetPublicUrl(string path) => $"{_settings.PublicBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

        public async Task<PublishedPlacement?> GetPublishedAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                var properties = await clients.Container.GetBlobClient(path).GetPropertiesAsync(cancellationToken: cancellationToken);
                var metadata = new Dictionary<string, string>(properties.Value.Metadata, StringComparer.OrdinalIgnoreCase);

                return new PublishedPlacement(
                    metadata.GetValueOrDefault(FingerprintKey),
                    metadata.GetValueOrDefault(SourceKey),
                    DateTimeOffset.TryParse(metadata.GetValueOrDefault(PublishedAtKey), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var publishedAt) ? publishedAt : null);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<string> UploadAsync(string path, byte[] content, PlacementMetadata metadata, CancellationToken cancellationToken = default)
        {
            var response = await clients.Container.GetBlobClient(path).UploadAsync(new BinaryData(content), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "image/webp",
                    CacheControl = _settings.CacheControl,
                },
                Metadata = new Dictionary<string, string>
                {
                    [FingerprintKey] = metadata.Fingerprint,
                    [SourceKey] = metadata.Source.ToString(),
                    [PhotoIdKey] = metadata.PhotoId?.ToString() ?? string.Empty,
                    [SourceWidthKey] = metadata.SourceWidth.ToString(CultureInfo.InvariantCulture),
                    [PublishedAtKey] = metadata.PublishedAt.ToString("O", CultureInfo.InvariantCulture),
                },
            }, cancellationToken);

            return response.Value.ETag.ToString();
        }

        /// <summary>
        /// The lock blob is empty, so its anonymous readability doesn't matter
        /// </summary>
        public async Task<IPlacementLock?> TryAcquireLockAsync(string path, CancellationToken cancellationToken = default)
        {
            var blob = clients.Container.GetBlobClient(path);
            try
            {
                await blob.UploadAsync(BinaryData.Empty, new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                }, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                // Already exists (412), or exists with a lease on it (409)
            }

            var lease = blob.GetBlobLeaseClient();
            try
            {
                await lease.AcquireAsync(LeaseDuration, cancellationToken: cancellationToken);
                return new LeaseLock(lease);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                return null;
            }
        }

        private sealed class LeaseLock(BlobLeaseClient lease) : IPlacementLock
        {
            public async Task RenewAsync(CancellationToken cancellationToken = default) =>
                await lease.RenewAsync(cancellationToken: cancellationToken);

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await lease.ReleaseAsync();
                }
                catch (RequestFailedException)
                {
                    // Expired already; nothing to release
                }
            }
        }
    }
}
