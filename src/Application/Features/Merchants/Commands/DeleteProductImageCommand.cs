using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Merchants.Commands
{
    public record DeleteProductImageRequest();
    public class DeleteProductImageCommand(int productImageId) : IRequest<bool>
    {
        public int ProductImageId { get; set; } = productImageId;

        public class Validator : AbstractValidator<DeleteProductImageCommand>
        {
            private readonly ElysianContext _context;
            public Validator(ElysianContext context)
            {
                _context = context;

                RuleFor(v => v.ProductImageId)
                    .NotEmpty()
                    .MustAsync(BeExistingProduct)
                        .WithMessage("No matching record found. The ID may be incorrect, or the record has been deleted.");
            }

            public async Task<bool> BeExistingProduct(int productImageId,
                CancellationToken cancellationToken)
            {
                return await _context.ProductImages.AnyAsync(p => p.ProductImageId == productImageId && !p.IsDeleted, cancellationToken: cancellationToken);
            }
        }

        public class Handler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<DeleteProductImageCommand, bool>
        {
            public async Task<bool> Handle(DeleteProductImageCommand request, CancellationToken cancellationToken)
            {
                if (!claimsPrincipalAccessor.IsAuthenticated)
                {
                    throw new ForbiddenAccessException();
                }

                var image = await context.ProductImages.SingleOrDefaultAsync(i => i.ProductImageId == request.ProductImageId && !i.IsDeleted) 
                    ?? throw new NotFoundException();
                image.IsDeleted = true;
                await context.SaveChangesAsync(cancellationToken);

                return true;
            }
        }
    }
}
