using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Photos.Queries
{
    /// <summary>
    /// Admin view of every photo regardless of processing status
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoRead)]
    public record GetPhotosQuery : IRequest<List<PhotoModel>>;

    public class GetPhotosQueryHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetPhotosQuery, List<PhotoModel>>
    {
        public async Task<List<PhotoModel>> Handle(GetPhotosQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            var photos = await context.Photos.AsNoTracking()
                .OrderBy(p => p.Category)
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken);

            return photos.Select(p => p.ToModel(photoStorage, tenantIdentifier)).ToList();
        }
    }
}
