using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Features.Stories.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Stories.Queries
{
    /// <summary>
    /// Public, anonymous list of published stories (optionally one category), newest event first;
    /// stories without a date fall back to when they were published
    /// </summary>
    public record GetStoriesQuery(string? Category = null) : IRequest<List<StorySummaryModel>>;

    public class GetStoriesQueryHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetStoriesQuery, List<StorySummaryModel>>
    {
        public async Task<List<StorySummaryModel>> Handle(GetStoriesQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            var query = context.Stories.AsNoTracking().Where(s => s.Status == StoryStatus.Published);
            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                query = query.Where(s => s.Category == request.Category);
            }

            var stories = await query.Include(s => s.Photos).AsSplitQuery().ToListAsync(cancellationToken);
            var placements = await context.GetPlacementLookupAsync(cancellationToken);

            return stories
                .OrderByDescending(s => s.EventDate.HasValue
                    ? new DateTimeOffset(s.EventDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                    : s.PublishedAt ?? s.CreatedAt)
                .Select(s => s.ToSummaryModel(photoStorage, tenantIdentifier, placements))
                .ToList();
        }
    }
}
