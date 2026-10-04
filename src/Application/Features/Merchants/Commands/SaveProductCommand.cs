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
    [Authorize(Policy = PolicyNames.ProductWrite)]
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

            RuleFor(v => v.SaveProductRequest.Price)
                .GreaterThanOrEqualTo(0).WithMessage("Price can't be negative.");

            When(v => v.SaveProductRequest?.ProductTypeId == (int)ProductTypes.Session, () =>
            {
                RuleFor(v => v.SaveProductRequest.SerialNumber)
                    .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
                        .WithMessage("Use lowercase letters, numbers and hyphens for the URL slug, e.g. classic-portrait.")
                    .MaximumLength(100);

                RuleFor(v => v.SaveProductRequest.Session)
                    .NotNull().WithMessage("Session details are required.");

                When(v => v.SaveProductRequest.Session != null, () =>
                {
                    RuleFor(v => v.SaveProductRequest.Session!.DurationMinutes)
                        .InclusiveBetween(15, 720).WithMessage("Duration must be between 15 minutes and 12 hours.");
                    RuleFor(v => v.SaveProductRequest.Session!.Collection)
                        .NotEmpty().WithMessage("Choose the Investment section this session is listed under.")
                        .MaximumLength(100);
                    RuleFor(v => v.SaveProductRequest.Session!.Location)
                        .MaximumLength(200);
                    RuleFor(v => v.SaveProductRequest.Session!.PortfolioCategory)
                        .MaximumLength(64);
                    RuleFor(v => v.SaveProductRequest.Session!.CoverPhotoId)
                        .MustAsync(BeExistingPhoto).WithMessage("The cover photo no longer exists. Choose another.");
                });
            });
        }

        public async Task<bool> BeExistingPhoto(Guid? photoId, CancellationToken cancellationToken)
        {
            return !photoId.HasValue || await _context.Photos.AnyAsync(p => p.PhotoId == photoId, cancellationToken);
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
                    Grade = request.SaveProductRequest.Grade ?? string.Empty,
                    Code = request.SaveProductRequest.Code,
                    Sku = request.SaveProductRequest.Sku,
                    DefaultTaxRates = true,
                    LookupCode = request.SaveProductRequest.LookupCode,
                    MerchantId = merchantId,
                    ProductTypeId = request.SaveProductRequest.ProductTypeId,
                    PriceTypeId = request.SaveProductRequest.PriceTypeId,
                    UnitTypeId = request.SaveProductRequest.UnitTypeId,
                    Price = request.SaveProductRequest.Price,
                };
                context.Add(product);
            }
            else
            {
                product.SerialNumber = request.SaveProductRequest.SerialNumber;
                product.Name = request.SaveProductRequest.Name;
                product.Grade = request.SaveProductRequest.Grade ?? string.Empty;
                product.Description = request.SaveProductRequest.Description;
                product.ProductTypeId = request.SaveProductRequest.ProductTypeId;
                product.PriceTypeId = request.SaveProductRequest.PriceTypeId;
                product.Price = request.SaveProductRequest.Price;
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
            await SaveSessionAsync(product, request.SaveProductRequest.Session, cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            // TODO: mapper
            return product;
        }

        /// <summary>
        /// Keeps the session details in step with the product type: stored for sessions, removed otherwise
        /// </summary>
        private async Task SaveSessionAsync(Product product, SaveProductSession? details, CancellationToken cancellationToken)
        {
            var session = await context.ProductSessions.SingleOrDefaultAsync(s => s.ProductId == product.ProductId, cancellationToken);

            if (product.ProductTypeId != (int)ProductTypes.Session || details == null)
            {
                if (session != null)
                {
                    context.ProductSessions.Remove(session);
                }
                return;
            }

            if (session == null)
            {
                session = new ProductSession { ProductId = product.ProductId };
                context.ProductSessions.Add(session);
            }

            session.DurationMinutes = details.DurationMinutes;
            session.Location = string.IsNullOrWhiteSpace(details.Location) ? null : details.Location.Trim();
            session.Collection = details.Collection.Trim();
            session.Features = details.Features?.Select(f => f.Trim()).Where(f => f.Length > 0).ToList() ?? [];
            session.PortfolioCategory = string.IsNullOrWhiteSpace(details.PortfolioCategory) ? null : details.PortfolioCategory.Trim();
            session.CoverPhotoId = details.CoverPhotoId;
            session.SortOrder = details.SortOrder;
            session.IsBookable = details.IsBookable;
        }
    }
}
