using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Context;
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

        public class Handler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<DeleteProductImageCommand, bool>
        {
            public async Task<bool> Handle(DeleteProductImageCommand request, CancellationToken cancellationToken)
            {
                var image = await context.ProductImages.SingleOrDefaultAsync(i => i.ProductImageId == request.ProductImageId && !i.IsDeleted) 
                    ?? throw new NotFoundException();
                image.IsDeleted = true;
                await context.SaveChangesAsync(cancellationToken);

                return true;
            }
        }
    }
}
