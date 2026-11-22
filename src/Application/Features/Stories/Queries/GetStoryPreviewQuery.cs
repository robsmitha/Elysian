using Elysian.Application.Exceptions;
using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Features.Stories.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Stories.Queries
{
    /// <summary>
    /// Anonymous read of one story in any status, drafts included, for whoever holds an unexpired token from
    /// <c>CreateStoryPreviewLinkCommand</c>. Shaped like the public story, so a site renders it with the same
    /// page. Hosts must not cache the response. A bad or expired token is "not found", with no hint of why.
    /// </summary>
    public record GetStoryPreviewQuery(string? Token) : IRequest<StoryModel>;

    public class GetStoryPreviewQueryHandler(ElysianContext context, IPhotoStorage photoStorage, IStoryPreviewTokens tokens,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetStoryPreviewQuery, StoryModel>
    {
        private const string NotFound = "This preview link is not valid or has expired.";

        public async Task<StoryModel> Handle(GetStoryPreviewQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            var storyId = tokens.Read(tenantIdentifier, request.Token) ?? throw new NotFoundException(NotFound);

            var story = await context.Stories.AsNoTracking()
                .Include(s => s.Photos)
                .SingleOrDefaultAsync(s => s.StoryId == storyId, cancellationToken)
                ?? throw new NotFoundException(NotFound);

            var placements = await context.GetPlacementLookupAsync(cancellationToken, story.Photos.Select(p => p.PhotoId).ToArray());
            return story.ToModel(photoStorage, tenantIdentifier, placements);
        }
    }
}
