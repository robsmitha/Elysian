using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    public class AzurePhotoStorage(BlobServiceClient blobServiceClient,
        IOptions<AzureStorageSettings> azureStorageSettings,
        IOptions<PhotoStorageSettings> photoStorageSettings, TenantImageHost? imageHost = null) : IPhotoStorage
    {
        private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

        private static volatile bool containersEnsured;

        private readonly PhotoStorageSettings _settings = photoStorageSettings.Value;
        private readonly BlobContainerClient _originals = blobServiceClient.GetBlobContainerClient(photoStorageSettings.Value.OriginalsContainer);
        private readonly BlobContainerClient _public = blobServiceClient.GetBlobContainerClient(photoStorageSettings.Value.PublicContainer);

        public async Task<Uri> CreateOriginalUploadUriAsync(string blobName, CancellationToken cancellationToken = default)
        {
            await EnsureContainersAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _originals.Name,
                BlobName = blobName,
                Resource = "b",
                StartsOn = now.AddMinutes(-5),
                ExpiresOn = now.AddMinutes(_settings.UploadUrlLifetimeMinutes)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

            var sasToken = sasBuilder.ToSasQueryParameters(
                new StorageSharedKeyCredential(blobServiceClient.AccountName, azureStorageSettings.Value.AccountKey));

            return new BlobUriBuilder(_originals.GetBlobClient(blobName).Uri) { Sas = sasToken }.ToUri();
        }

        public async Task<Stream> OpenOriginalAsync(string blobName, CancellationToken cancellationToken = default)
        {
            return await _originals.GetBlobClient(blobName).OpenReadAsync(cancellationToken: cancellationToken);
        }

        public async Task DeleteOriginalAsync(string blobName, CancellationToken cancellationToken = default)
        {
            await _originals.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }

        public async Task<int> DeleteOriginalsAsync(string prefix, CancellationToken cancellationToken = default)
        {
            RequireFolder(prefix);
            var deleted = 0;
            await foreach (var blob in _originals.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                await _originals.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
                deleted++;
            }
            return deleted;
        }

        public Task<Uri> CreateOriginalDownloadUriAsync(string blobName, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _originals.Name,
                BlobName = blobName,
                Resource = "b",
                StartsOn = now.AddMinutes(-5),
                ExpiresOn = now.Add(lifetime),
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasToken = sasBuilder.ToSasQueryParameters(
                new StorageSharedKeyCredential(blobServiceClient.AccountName, azureStorageSettings.Value.AccountKey));

            return Task.FromResult(new BlobUriBuilder(_originals.GetBlobClient(blobName).Uri) { Sas = sasToken }.ToUri());
        }

        /// <summary>
        /// A delete by prefix is only ever a whole folder: an empty prefix would be the whole container
        /// </summary>
        private static void RequireFolder(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix) || !prefix.EndsWith('/') || prefix.Trim('/').Length == 0)
            {
                throw new ArgumentException("Files are deleted a folder at a time: the prefix must name one and end with a slash.", nameof(prefix));
            }
        }

        public async Task UploadVariantAsync(string blobName, byte[] content, string contentType, CancellationToken cancellationToken = default)
        {
            await EnsureContainersAsync(cancellationToken);

            await _public.GetBlobClient(blobName).UploadAsync(new BinaryData(content), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType,
                    CacheControl = ImmutableCacheControl
                }
            }, cancellationToken);
        }

        public async Task DeleteVariantsAsync(string prefix, CancellationToken cancellationToken = default)
        {
            await foreach (var blob in _public.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                await _public.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
            }
        }

        public async Task<IReadOnlySet<string>> ListVariantsAsync(string prefix, CancellationToken cancellationToken = default)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            await foreach (var blob in _public.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                names.Add(blob.Name);
            }
            return names;
        }

        public string GetPublicUrl(string blobPath)
        {
            // A site drawn with a template is served from the platform's images host
            var baseUrl = imageHost?.ContainerUrl(_public.Name)
                ?? (string.IsNullOrWhiteSpace(_settings.PublicBaseUrl) ? _public.Uri.ToString() : _settings.PublicBaseUrl);

            return $"{baseUrl.TrimEnd('/')}/{blobPath.TrimStart('/')}";
        }

        private async Task EnsureContainersAsync(CancellationToken cancellationToken)
        {
            if (containersEnsured)
            {
                return;
            }

            await _originals.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

            // Variants are served straight to browsers/CDN: anonymous read of individual blobs, no listing.
            // Requires "Allow Blob anonymous access" on the storage account.
            await _public.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

            containersEnsured = true;
        }
    }
}
