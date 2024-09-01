using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Elysian.Application.Features.Merchants.Commands
{
    [Authorize]
    public record DeleteProductCommand(int ProductId) : IRequest<bool>;

    public class DeleteProductCommandValidator : AbstractValidator<DeleteProductCommand>
    {
        private readonly ElysianContext _context;
        public DeleteProductCommandValidator(ElysianContext context)
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

    public class DeleteProductCommandHandler(ElysianContext context) : IRequestHandler<DeleteProductCommand, bool>
    {
        public async Task<bool> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
        {
            var (product, images) = await GetProductExtensionsAsync(c => c.ProductId == request.ProductId);
            product.IsDeleted = true;
            images.ForEach(i => i.IsDeleted = true);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        private async Task<(Product, List<ProductImage>)> GetProductExtensionsAsync(Expression<Func<Product, bool>> predicate)
        {
            var product = await context.Products.SingleOrDefaultAsync(predicate) ?? throw new NotFoundException();
            var images = await context.ProductImages.Where(i => i.ProductId == product.ProductId).ToListAsync();
            return (product, images);
        }
    }
}
