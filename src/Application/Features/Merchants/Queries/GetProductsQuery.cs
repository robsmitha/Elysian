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
    [Authorize(Policy = PolicyNames.ProductRead)]
    /// <param name="ProductTypeId">Only products of this type, e.g. sessions; all products when null</param>
    public record GetProductsQuery(int? ProductTypeId = null) : IRequest<List<Product>>;

    public class GetProductsQueryHander(ElysianContext context) : IRequestHandler<GetProductsQuery, List<Product>>
    {
        public async Task<List<Product>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
        {
            var query = context.Products.AsNoTracking();
            if (request.ProductTypeId.HasValue)
            {
                query = query.Where(p => p.ProductTypeId == request.ProductTypeId);
            }
            return await query.ToListAsync(cancellationToken);
        }
    }
}
