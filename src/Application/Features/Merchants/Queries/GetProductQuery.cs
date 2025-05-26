using Elysian.Application.Exceptions;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Elysian.Application.Features.Merchants.Queries
{
    [Authorize(Policy = PolicyNames.ProductRead)]
    public record GetProductQuery(int ProductId) : IRequest<GetProductQueryResponse>;

    public class GetProductQueryValidator : AbstractValidator<GetProductQuery>
    {
        private readonly ElysianContext _context;
        public GetProductQueryValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.ProductId)
                .NotEmpty()
                .MustAsync(BeExistingProduct)
                    .WithMessage("No matching record found. The ID may be incorrect, or the record has been deleted.");
        }

        public async Task<bool> BeExistingProduct(int productId,
            CancellationToken cancellationToken)
        {
            return await _context.Products.AnyAsync(p => p.ProductId == productId, cancellationToken: cancellationToken);
        }
    }

    public record GetProductQueryResponse(Product Product, List<ProductImage> Images);
    public class GetProductQueryHandler(ElysianContext context)
        : IRequestHandler<GetProductQuery, GetProductQueryResponse>
    {
        public async Task<GetProductQueryResponse> Handle(GetProductQuery request, CancellationToken cancellationToken)
        {
            var (product, images) = await GetProductExtensionsAsync(c => c.ProductId == request.ProductId);

            return new GetProductQueryResponse(product, images);
        }

        private async Task<(Product, List<ProductImage>)> GetProductExtensionsAsync(Expression<Func<Product, bool>> predicate)
        {
            var product = await context.Products.SingleOrDefaultAsync(predicate) ?? throw new NotFoundException();
            var images = await context.ProductImages.Where(i => i.ProductId == product.ProductId).ToListAsync();
            return (product, images);
        }
    }
}
