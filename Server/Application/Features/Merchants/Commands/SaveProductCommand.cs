using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Merchants.Commands
{
    public record SaveProductRequest(string Name, string Description, string SerialNumber,
            string Grade, List<SaveProductImage> AddImages, int? ProductId = null);
    public record SaveProductImage(string FileName, long FileSize, Guid StorageId);

    public class SaveProductCommand(SaveProductRequest saveProductRequest) : IRequest<Product>
    {
        public SaveProductRequest SaveProductRequest = saveProductRequest;

        public class Handler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SaveProductCommand, Product>
        {
            public async Task<Product> Handle(SaveProductCommand request, CancellationToken cancellationToken)
            {
                // TODO: validator
                if (!request.SaveProductRequest.ProductId.HasValue && await context.Products.AnyAsync(c => !c.IsDeleted && c.SerialNumber == request.SaveProductRequest.SerialNumber))
                {
                    throw new CustomValidationException();
                }

                // TODO: validator
                if (request.SaveProductRequest.ProductId.HasValue && await context.Products.AnyAsync(c => !c.IsDeleted && c.SerialNumber == request.SaveProductRequest.SerialNumber && c.ProductId != request.SaveProductRequest.ProductId))
                {
                    throw new CustomValidationException();
                }

                var product = request.SaveProductRequest.ProductId.HasValue
                    ? await context.Products.SingleOrDefaultAsync(c => c.ProductId == request.SaveProductRequest.ProductId)
                    : null;

                if (product == null)
                {
                    product = new Product
                    {
                        SerialNumber = request.SaveProductRequest.SerialNumber,
                        Name = request.SaveProductRequest.Name,
                        Description = request.SaveProductRequest.Description,
                        Grade = request.SaveProductRequest.Grade,
                        // TODO: interceptor
                        CreatedByUserId = claimsPrincipalAccessor.UserId,
                        CreatedAt = DateTime.UtcNow,
                        ModifiedByUserId = claimsPrincipalAccessor.UserId,
                        ModifiedAt = DateTime.UtcNow
                    };
                    context.Add(product);
                }
                else
                {
                    product.SerialNumber = request.SaveProductRequest.SerialNumber;
                    product.Name = request.SaveProductRequest.Name;
                    product.Grade = request.SaveProductRequest.Grade;
                    product.Description = request.SaveProductRequest.Description;
                    product.ModifiedByUserId = claimsPrincipalAccessor.UserId;
                    product.ModifiedAt = DateTime.UtcNow;
                }
                await context.SaveChangesAsync();

                foreach (var image in request.SaveProductRequest.AddImages)
                {
                    context.Add(new ProductImage
                    {
                        ProductId = product.ProductId,
                        FileName = image.FileName,
                        FileSize = image.FileSize,
                        AltText = image.FileName,
                        StorageId = image.StorageId,
                        ModifiedByUserId = claimsPrincipalAccessor.UserId,
                        ModifiedAt = DateTime.UtcNow,
                        CreatedByUserId = claimsPrincipalAccessor.UserId,
                        CreatedAt = DateTime.UtcNow,
                    });
                }
                await context.SaveChangesAsync(cancellationToken);
                
                // TODO: mapper
                return product;
            }
        }
    }
}
