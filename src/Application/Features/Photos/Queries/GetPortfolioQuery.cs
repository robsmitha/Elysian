using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Photos.Queries
{
    /// <summary>
    /// Public, anonymous read of every ready photo (optionally one category)
    /// </summary>
    public record GetPortfolioQuery(string? Category = null) : IRequest<List<PortfolioPhotoModel>>;

    public class GetPortfolioQueryHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetPortfolioQuery, List<PortfolioPhotoModel>>
    {
        public async Task<List<PortfolioPhotoModel>> Handle(GetPortfolioQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            var query = context.Photos.AsNoTracking().Where(p => p.Status == PhotoStatus.Ready);
            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                query = query.Where(p => p.Category == request.Category);
            }

            var photos = await query
                .OrderBy(p => p.Category)
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken);

            return photos.Select(p => p.ToPortfolioModel(photoStorage, tenantIdentifier)).ToList();
        }
    }
}
