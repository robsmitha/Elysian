using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Interfaces
{
    public interface IAzureStorageClient
    {
        Task<BlobContainerClient> GetBlobContainerClientAsync(string blobContainerName);
        Task<string> CreateSasTokenAsync(BlobContainerClient containerClient,
            string blobName, BlobSasPermissions permissions);
        Task<Uri> GetSasUriAsync(BlobContainerClient containerClient,
            string containerPrefix, Guid folderId,
            string fileName, BlobSasPermissions permissions);
    }
}