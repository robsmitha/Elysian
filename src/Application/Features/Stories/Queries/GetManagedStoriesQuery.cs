using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Features.Stories.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Stories.Queries
{
    /// <summary>
    /// Admin list of every story in any status, most recently edited first
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoRead)]
    public record GetManagedStoriesQuery : IRequest<List<ManagedStorySummaryModel>>;

    public class GetManagedStoriesQueryHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetManagedStoriesQuery, List<ManagedStorySummaryModel>>
    {
        public async Task<List<ManagedStorySummaryModel>> Handle(GetManagedStoriesQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            var stories = await context.Stories.AsNoTracking()
                .Include(s => s.Photos)
                .AsSplitQuery()
                .OrderByDescending(s => s.ModifiedAt)
                .ToListAsync(cancellationToken);

            var placements = await context.GetPlacementLookupAsync(cancellationToken);
            return stories.Select(s => s.ToManagedSummaryModel(photoStorage, tenantIdentifier, placements)).ToList();
        }
    }
}
