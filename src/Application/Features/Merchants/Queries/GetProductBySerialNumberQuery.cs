using Azure.Storage.Sas;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Elysian.Application.Features.Merchants.Queries
{
    public class GetProductBySerialNumberQuery(string serialNumber) : IRequest<GetProductBySerialNumberQuery.Response>
    {
        public string SerialNumber { get; set; } = serialNumber;

        public record Response(Product Product, List<Uri> ImagesUris);

        public class Handler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor, IAzureStorageClient azureStorageClient,
            IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor) 
            : IRequestHandler<GetProductBySerialNumberQuery, Response>
        {
            public async Task<Response> Handle(GetProductBySerialNumberQuery request, CancellationToken cancellationToken)
            {
                if (string.IsNullOrEmpty(request.SerialNumber))
                {
                    throw new CustomValidationException();
                }

                var tenantInfo = multiTenantContextAccessor.MultiTenantContext.TenantInfo;
                var (product, images) = await GetProductExtensionsAsync(c => c.SerialNumber == request.SerialNumber);
                var containerClient = await azureStorageClient.GetBlobContainerClientAsync("products");
                var sasUris = new List<Uri>();
                foreach (var image in images)
                {
                    var sasUri = await azureStorageClient.GetSasUriAsync(containerClient, tenantInfo.Identifier, image.StorageId, image.FileName, BlobSasPermissions.Read);
                    sasUris.Add(sasUri);
                }

                return new Response(product, sasUris);
            }

            private async Task<(Product, List<ProductImage>)> GetProductExtensionsAsync(Expression<Func<Product, bool>> predicate)
            {
                var product = await context.Products.SingleOrDefaultAsync(predicate) ?? throw new NotFoundException();
                var images = await context.ProductImages.Where(i => i.ProductId == product.ProductId && !i.IsDeleted).ToListAsync();
                return (product, images);
            }
        }
    }
}
