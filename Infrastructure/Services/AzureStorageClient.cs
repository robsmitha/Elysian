using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Azure.Storage;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using Azure.Storage.Blobs.Models;

namespace Elysian.Infrastructure.Services
{
    public class AzureStorageClient(IConfiguration configuration, BlobServiceClient blobServiceClient) 
        : IAzureStorageClient
    {
        public async Task<BlobContainerClient> GetBlobContainerClientAsync(string blobContainerName)
        {
            var containerClient = blobServiceClient.GetBlobContainerClient(blobContainerName);

            var createResponse = await containerClient.CreateIfNotExistsAsync();
            if (createResponse != null)
            {
                await CreateAccessPolicyAsync(containerClient);
            }

            return containerClient;
        }


        public async Task<string> CreateSasTokenAsync(BlobContainerClient containerClient,
            string blobName, BlobSasPermissions permissions)
        {
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync() && permissions.HasFlag(BlobSasPermissions.Read))
            {
                throw new NotFoundException();
            }

            var now = DateTime.UtcNow;
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                BlobName = blobName,
                Resource = "b",
                StartsOn = now.AddMinutes(-5),
                ExpiresOn = now.AddHours(24)
            };
            sasBuilder.SetPermissions(permissions);

            var accountKey = configuration.GetSection("Azure:AccountKey").Get<string>();
            var sasToken = sasBuilder.ToSasQueryParameters(
                new StorageSharedKeyCredential(blobServiceClient.AccountName, accountKey)).ToString();

            return sasToken;
        }

        public async Task<Uri> GetSasUriAsync(BlobContainerClient containerClient,
            string containerPrefix, Guid folderId,
            string fileName, BlobSasPermissions permissions)
        {
            var blobName = $"{containerPrefix}/{folderId}/{fileName}";
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync() && permissions.HasFlag(BlobSasPermissions.Read))
            {
                throw new NotFoundException();
            }

            var now = DateTime.UtcNow;
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                BlobName = blobName,
                Resource = "b",
                StartsOn = now.AddMinutes(-5),
                ExpiresOn = now.AddHours(24)
            };
            sasBuilder.SetPermissions(permissions);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);

            return sasUri;
        }

        private async Task CreateAccessPolicyAsync(BlobContainerClient containerClient)
        {
            var policy = await containerClient.GetAccessPolicyAsync();
            var permissions = policy.Value.SignedIdentifiers.ToList();

            var set = false;
            if (permissions.All(p => p.Id != "AdminAccessPolicy"))
            {
                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = containerClient.Name,
                    // Container
                    Resource = "c",
                    Identifier = "AdminAccessPolicy"
                };

                sasBuilder.SetPermissions(BlobAccountSasPermissions.All);
                permissions.Add(new BlobSignedIdentifier
                {
                    Id = sasBuilder.Identifier,
                    AccessPolicy = new BlobAccessPolicy
                    {
                        Permissions = sasBuilder.Permissions,
                    }
                });

                set = true;
            }

            if (permissions.All(p => p.Id != "PublicAccessPolicy"))
            {
                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = containerClient.Name,
                    // Container
                    Resource = "c",
                    Identifier = "PublicAccessPolicy"
                };

                sasBuilder.SetPermissions(BlobAccountSasPermissions.Read);
                permissions.Add(new BlobSignedIdentifier
                {
                    Id = sasBuilder.Identifier,
                    AccessPolicy = new BlobAccessPolicy
                    {
                        Permissions = sasBuilder.Permissions,
                    }
                });

                set = true;
            }

            if (set)
            {
                await containerClient.SetAccessPolicyAsync(permissions: permissions);
            }
        }
    }
}
