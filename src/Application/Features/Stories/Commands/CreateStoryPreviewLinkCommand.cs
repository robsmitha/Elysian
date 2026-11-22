using Elysian.Application.Exceptions;
using Elysian.Application.Features.Stories.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Stories.Commands
{
    /// <summary>
    /// Issues a short-lived token that opens one story in any status through <c>GetStoryPreviewQuery</c>, so
    /// someone who manages the site can preview a draft on the public site without signing in there.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoRead)]
    public record CreateStoryPreviewLinkCommand(Guid StoryId) : IRequest<StoryPreviewLinkModel>;

    public class CreateStoryPreviewLinkCommandHandler(ElysianContext context, IStoryPreviewTokens tokens,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<CreateStoryPreviewLinkCommand, StoryPreviewLinkModel>
    {
        public async Task<StoryPreviewLinkModel> Handle(CreateStoryPreviewLinkCommand request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            if (!await context.Stories.AnyAsync(s => s.StoryId == request.StoryId, cancellationToken))
            {
                throw new NotFoundException("No matching story found. The ID may be incorrect, or the story has been deleted.");
            }

            var (token, expiresAt) = tokens.Issue(tenantIdentifier, request.StoryId);
            return new StoryPreviewLinkModel(token, expiresAt);
        }
    }
}
