using Azure.Storage.Sas;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Elysian.Application.Features.Merchants.Queries
{
    public class GetProductBySerialNumberQuery(string serialNumber) : IRequest<GetProductBySerialNumberQuery.Response>
    {
        public string SerialNumber { get; set; } = serialNumber;
        // TODO: finbuckle
        public string TenantContainerPrefix { get; set; } = "";
        public string ProductImageContainerPrefix { get; set; } = "";

        public class Response
        {
            public Product Product { get; set; }
            public List<Uri> ImagesUris { get; set; }
        }

        public class Handler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor, IAzureStorageClient azureStorageClient) 
            : IRequestHandler<GetProductBySerialNumberQuery, Response>
        {
            public async Task<Response> Handle(GetProductBySerialNumberQuery request, CancellationToken cancellationToken)
            {
                var (product, images) = await GetProductExtensionsAsync(c => c.SerialNumber == request.SerialNumber);
                var containerClient = await azureStorageClient.GetBlobContainerClientAsync(request.ProductImageContainerPrefix);
                var sasUris = new List<Uri>();
                foreach (var image in images)
                {
                    var sasUri = await azureStorageClient.GetSasUriAsync(containerClient, request.TenantContainerPrefix, image.StorageId, image.FileName, BlobSasPermissions.Read);
                    sasUris.Add(sasUri);
                }

                return new Response
                {
                    Product = product,
                    ImagesUris = sasUris
                };
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
