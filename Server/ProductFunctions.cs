using Elysian.Application.Exceptions;
using Elysian.Application.Extensions;
using Elysian.Application.Features.Merchants.Commands;
using Elysian.Application.Features.Merchants.Queries;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian
{
    public class ProductFunctions(ILogger<ProductFunctions> logger, ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor, 
        IMediator mediator)
    {
        [Function("GetProducts")]
        public async Task<HttpResponseData> GetProducts([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            if (!claimsPrincipalAccessor.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            var products = await context.Products.Where(c => !c.IsDeleted).ToListAsync();
            return await req.WriteJsonResponseAsync(products);
        }

        [Function("GetProduct")]
        public async Task<HttpResponseData> GetProduct([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            if (!claimsPrincipalAccessor.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            if (!int.TryParse(req.Query["productId"], out var productId))
            {
                throw new CustomValidationException();
            }

            logger.LogInformation("Get Product by Id request [ProductId: {productId}]", productId);

            var (product, images) = await mediator.Send(new GetProductQuery(productId));

            return product == null
                ? throw new NotFoundException()
                : await req.WriteJsonResponseAsync(new
                {
                    product,
                    images
                });
        }

        [Function("GetProductBySerialNumber")]
        public async Task<HttpResponseData> GetProductBySerialNumber([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var serialNumber = req.Query["serialNumber"];

            logger.LogInformation("Get Product By Serial Number request [SerialNumber: {serialNumber}]", serialNumber);

            var (product, sasUris) = await mediator.Send(new GetProductBySerialNumberQuery(serialNumber));

            return product == null
                ? throw new NotFoundException()
                : await req.WriteJsonResponseAsync(new
                {
                    product,
                    sasUris
                });
        }

        [Function("SaveProduct")]
        public async Task<HttpResponseData> SaveProduct([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            if (!claimsPrincipalAccessor.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            var saveProduct = await req.DeserializeBodyAsync<SaveProductRequest>();
            var product = await mediator.Send(new SaveProductCommand(saveProduct));

            return await req.WriteJsonResponseAsync(product);
        }
        public record DeleteProductRequest(int ProductId);
        [Function("DeleteProduct")]
        public async Task<HttpResponseData> DeleteProduct([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            if (!claimsPrincipalAccessor.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            var deleteProduct = await req.DeserializeBodyAsync<DeleteProductRequest>();
            logger.LogInformation("Delete Product by Id request [ProductId: {productId}]", deleteProduct.ProductId);

            await mediator.Send(new DeleteProductCommand(deleteProduct.ProductId));

            // TODO: Create timer trigger that deletes images from azure

            return await req.WriteJsonResponseAsync(new
            {
                success = true
            });
        }
        public record DeleteProductImageRequest(int ProductImageId);
        [Function("DeleteProductImage")]
        public async Task<HttpResponseData> DeleteProductImage([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            if (!claimsPrincipalAccessor.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            var deleteProductImage = await req.DeserializeBodyAsync<DeleteProductImageRequest>();
            logger.LogInformation("Delete Product Image by Id request [CardImageId: {CardImageId}]", deleteProductImage.ProductImageId);

            await mediator.Send(new DeleteProductImageCommand(deleteProductImage.ProductImageId));

            return await req.WriteJsonResponseAsync(new
            {
                success = true
            });
        }
    }
}
