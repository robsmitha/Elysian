using Elysian.Application.Exceptions;
using Elysian.Application.Features.Merchants.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Merchants.Commands
{
    public class DeleteProductCommand(int productId) : IRequest<bool>
    {
        public int ProductId { get; set; } = productId;

        public class Validator : AbstractValidator<DeleteProductCommand>
        {
            private readonly ElysianContext _context;
            public Validator(ElysianContext context)
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
                return await _context.Products.AnyAsync(p => p.ProductId == productId && !p.IsDeleted, cancellationToken: cancellationToken);
            }
        }

        public class Handler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<DeleteProductCommand, bool>
        {
            public async Task<bool> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
            {
                if (!claimsPrincipalAccessor.IsAuthenticated)
                {
                    throw new ForbiddenAccessException();
                }

                var (product, images) = await GetProductExtensionsAsync(c => c.ProductId == request.ProductId);
                product.IsDeleted = true;
                images.ForEach(i => i.IsDeleted = true);
                await context.SaveChangesAsync(cancellationToken);
                return true;
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
