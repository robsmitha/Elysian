using Elysian.Application.Exceptions;
using Elysian.Application.Features.Merchants.Models;
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

    public record GetProductQueryResponse(Product Product, List<ProductImage> Images)
    {
        /// <summary>
        /// Session details for session products, in the shape the save request takes
        /// </summary>
        public SaveProductSession? Session { get; init; }
    }
    public class GetProductQueryHandler(ElysianContext context)
        : IRequestHandler<GetProductQuery, GetProductQueryResponse>
    {
        public async Task<GetProductQueryResponse> Handle(GetProductQuery request, CancellationToken cancellationToken)
        {
            var (product, images) = await GetProductExtensionsAsync(c => c.ProductId == request.ProductId);

            var session = await context.ProductSessions.AsNoTracking()
                .Where(s => s.ProductId == product.ProductId)
                .Select(s => new SaveProductSession(s.DurationMinutes, s.Location, s.Collection, s.Features, s.PortfolioCategory,
                    s.CoverPhotoId, s.SortOrder, s.IsBookable))
                .SingleOrDefaultAsync(cancellationToken);

            return new GetProductQueryResponse(product, images) { Session = session };
        }

        private async Task<(Product, List<ProductImage>)> GetProductExtensionsAsync(Expression<Func<Product, bool>> predicate)
        {
            var product = await context.Products.SingleOrDefaultAsync(predicate) ?? throw new NotFoundException();
            var images = await context.ProductImages.Where(i => i.ProductId == product.ProductId).ToListAsync();
            return (product, images);
        }
    }
}
