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
        IOptions<PhotoStorageSettings> photoStorageSettings) : IPhotoStorage
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

        public string GetPublicUrl(string blobPath)
        {
            var baseUrl = string.IsNullOrWhiteSpace(_settings.PublicBaseUrl)
                ? _public.Uri.ToString()
                : _settings.PublicBaseUrl;

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
