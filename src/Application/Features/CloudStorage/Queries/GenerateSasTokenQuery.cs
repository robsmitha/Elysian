using Azure.Storage.Sas;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.CloudStorage.Queries
{
    public class GenerateSasTokenQuery(string fileName) : IRequest<GenerateSasTokenQuery.Response>
    {
        public string FileName { get; set; } = fileName;

        public record Response(string AccountName, string ContainerName, string BlobName,
            Guid StorageId, string SasToken);

        public class Handler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor, IAzureStorageClient azureStorageClient,
            IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
            : IRequestHandler<GenerateSasTokenQuery, Response>
        {
            public async Task<Response> Handle(GenerateSasTokenQuery request, CancellationToken cancellationToken)
            {
                var tenantInfo = multiTenantContextAccessor.MultiTenantContext.TenantInfo;
                var storageId = Guid.NewGuid();
                var containerClient = await azureStorageClient.GetBlobContainerClientAsync("products");
                var blobName = $"{tenantInfo.Identifier}/{storageId}/{request.FileName}";
                var sasToken = await azureStorageClient.CreateSasTokenAsync(containerClient, blobName,
                    BlobSasPermissions.Create | BlobSasPermissions.Add | BlobSasPermissions.Write);

                return new Response(containerClient.AccountName, containerClient.Name, blobName, storageId, sasToken);
            }
        }
    }
}
