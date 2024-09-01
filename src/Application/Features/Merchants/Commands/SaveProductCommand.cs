using Elysian.Application.Exceptions;
using Elysian.Application.Features.Merchants.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Merchants.Commands
{
    [Authorize]
    public record SaveProductCommand(SaveProductRequest SaveProductRequest) : IRequest<Product>;

    public class SaveProductCommandValidator : AbstractValidator<SaveProductCommand>
    {
        private readonly ElysianContext _context;
        public SaveProductCommandValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.SaveProductRequest)
                .NotEmpty()
                .MustAsync(BeUniqueSerialNumber)
                    .WithMessage("The serial number must be unique. The provided serial number already exists in the system");

            RuleFor(v => v.SaveProductRequest.ProductId)
                .MustAsync(BeValidProductId)
                    .WithMessage("No matching record found. The ID may be incorrect, or the record has been deleted.");
        }

        public async Task<bool> BeUniqueSerialNumber(SaveProductRequest saveProductRequest,
            CancellationToken cancellationToken)
        {
            var query = _context.Products.Where(c => c.SerialNumber == saveProductRequest.SerialNumber);

            return saveProductRequest.ProductId.HasValue
                ? !await query.AnyAsync(c => c.ProductId != saveProductRequest.ProductId, cancellationToken)
                : !await query.AnyAsync(cancellationToken);

        }

        public async Task<bool> BeValidProductId(int? productId,
            CancellationToken cancellationToken)
        {
            return !productId.HasValue || await _context.Products.AnyAsync(p => p.ProductId == productId, cancellationToken: cancellationToken);
        }
    }

    public class SaveProductCommandHandler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor,
        IMultiTenantContextAccessor<ElysianTenantInfo> muliTenantContextAccessor) : IRequestHandler<SaveProductCommand, Product>
    {
        public async Task<Product> Handle(SaveProductCommand request, CancellationToken cancellationToken)
        {
            if (!claimsPrincipalAccessor.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            var product = request.SaveProductRequest.ProductId.HasValue
                ? await context.Products.SingleOrDefaultAsync(c => c.ProductId == request.SaveProductRequest.ProductId)
                : null;

            if (product == null)
            {
                var merchantId = await context.Merchants
                    .Where(m => m.MerchantIdentifier == muliTenantContextAccessor.MultiTenantContext.TenantInfo.Identifier)
                    .Select(m => m.MerchantId)
                    .FirstOrDefaultAsync();

                if (merchantId == 0)
                {
                    throw new NotFoundException();
                }

                product = new Product
                {
                    SerialNumber = request.SaveProductRequest.SerialNumber,
                    Name = request.SaveProductRequest.Name,
                    Description = request.SaveProductRequest.Description,
                    Grade = request.SaveProductRequest.Grade,
                    Code = request.SaveProductRequest.Code,
                    Sku = request.SaveProductRequest.Sku,
                    DefaultTaxRates = true,
                    LookupCode = request.SaveProductRequest.LookupCode,
                    MerchantId = merchantId,
                    ProductTypeId = request.SaveProductRequest.ProductTypeId,
                    PriceTypeId = request.SaveProductRequest.PriceTypeId,
                    UnitTypeId = request.SaveProductRequest.UnitTypeId,
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
                });
            }
            await context.SaveChangesAsync(cancellationToken);

            // TODO: mapper
            return product;
        }
    }
}
