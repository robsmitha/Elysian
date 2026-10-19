using Elysian.Application.Exceptions;
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
    /// Admin read of one story in any status, for the editor and for previewing drafts
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoRead)]
    public record GetManagedStoryQuery(Guid StoryId) : IRequest<ManagedStoryModel>;

    public class GetManagedStoryQueryHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetManagedStoryQuery, ManagedStoryModel>
    {
        public async Task<ManagedStoryModel> Handle(GetManagedStoryQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            var story = await context.Stories.AsNoTracking()
                .Include(s => s.Photos)
                .SingleOrDefaultAsync(s => s.StoryId == request.StoryId, cancellationToken)
                ?? throw new NotFoundException("No matching story found. The ID may be incorrect, or the story has been deleted.");

            var placements = await context.GetPlacementLookupAsync(cancellationToken, story.Photos.Select(p => p.PhotoId).ToArray());
            return story.ToManagedModel(photoStorage, tenantIdentifier, placements);
        }
    }
}
