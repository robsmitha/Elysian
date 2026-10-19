using Elysian.Application.Exceptions;
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
    /// Public, anonymous read of one published or unlisted story by slug; drafts are not found
    /// </summary>
    public record GetStoryQuery(string Slug) : IRequest<StoryModel>;

    public class GetStoryQueryHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetStoryQuery, StoryModel>
    {
        public async Task<StoryModel> Handle(GetStoryQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            var story = await context.Stories.AsNoTracking()
                .Include(s => s.Photos)
                .Where(s => s.Slug == request.Slug && s.Status != StoryStatus.Draft)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException($"No story found at \"{request.Slug}\".");

            var placements = await context.GetPlacementLookupAsync(cancellationToken, story.Photos.Select(p => p.PhotoId).ToArray());
            return story.ToModel(photoStorage, tenantIdentifier, placements);
        }
    }
}
