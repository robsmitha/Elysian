using Azure.Storage.Sas;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Elysian.Application.Features.Merchants.Queries
{
    public record GetProductBySerialNumberQuery(string SerialNumber) : IRequest<GetProductBySerialNumberQueryResponse>;

    public class GetProductBySerialNumberQueryValidator : AbstractValidator<GetProductBySerialNumberQuery>
    {
        private readonly ElysianContext _context;
        public GetProductBySerialNumberQueryValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.SerialNumber)
                .NotEmpty()
                .MustAsync(HaveValidSerialNumber)
                    .WithMessage("No records found for the provided serial number. Please verify the number and try again.");
        }

        public async Task<bool> HaveValidSerialNumber(string serialNumber,
            CancellationToken cancellationToken)
        {
            return await _context.Products.AnyAsync(p => p.SerialNumber == serialNumber, cancellationToken: cancellationToken);
        }
    }

    public record GetProductBySerialNumberQueryResponse(Product Product, List<Uri> ImagesUris);

    public class GetProductBySerialNumberQueryHandler(ElysianContext context, IAzureStorageClient azureStorageClient,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetProductBySerialNumberQuery, GetProductBySerialNumberQueryResponse>
    {
        public async Task<GetProductBySerialNumberQueryResponse> Handle(GetProductBySerialNumberQuery request, CancellationToken cancellationToken)
        {
            var tenantInfo = multiTenantContextAccessor.MultiTenantContext.TenantInfo;
            var (product, images) = await GetProductExtensionsAsync(c => c.SerialNumber == request.SerialNumber);
            var containerClient = await azureStorageClient.GetBlobContainerClientAsync("products");
            var sasUris = new List<Uri>();
            foreach (var image in images)
            {
                var sasUri = await azureStorageClient.GetSasUriAsync(containerClient, tenantInfo.Identifier, image.StorageId, image.FileName, BlobSasPermissions.Read);
                sasUris.Add(sasUri);
            }

            return new GetProductBySerialNumberQueryResponse(product, sasUris);
        }

        private async Task<(Product, List<ProductImage>)> GetProductExtensionsAsync(Expression<Func<Product, bool>> predicate)
        {
            var product = await context.Products.SingleOrDefaultAsync(predicate) ?? throw new NotFoundException();
            var images = await context.ProductImages.Where(i => i.ProductId == product.ProductId).ToListAsync();
            return (product, images);
        }
    }
}
