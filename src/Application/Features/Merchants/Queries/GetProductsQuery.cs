using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Merchants.Queries
{
    [Authorize]
    public record GetProductsQuery : IRequest<List<Product>>;

    public class GetProductsQueryHander(ElysianContext context) : IRequestHandler<GetProductsQuery, List<Product>>
    {
        public async Task<List<Product>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
        {
            return await context.Products.AsNoTracking().ToListAsync(cancellationToken);
        }
    }
}
