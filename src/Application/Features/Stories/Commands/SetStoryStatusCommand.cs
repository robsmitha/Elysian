using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Features.Stories.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Stories.Commands
{
    /// <summary>
    /// Moves a story between Draft, Unlisted and Published. The first publish sets PublishedAt.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record SetStoryStatusCommand(Guid StoryId, string Status) : IRequest<ManagedStoryModel>;

    public class SetStoryStatusCommandValidator : AbstractValidator<SetStoryStatusCommand>
    {
        private readonly ElysianContext _context;
        public SetStoryStatusCommandValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.StoryId)
                .NotEmpty()
                .MustAsync(BeExistingStory)
                    .WithMessage("No matching story found. The ID may be incorrect, or the story has been deleted.");

            RuleFor(v => v.Status)
                .Must(s => Enum.TryParse<StoryStatus>(s, true, out _))
                    .WithMessage($"Status must be one of {string.Join(", ", Enum.GetNames<StoryStatus>())}.");
        }

        public async Task<bool> BeExistingStory(Guid storyId, CancellationToken cancellationToken)
        {
            return await _context.Stories.AnyAsync(s => s.StoryId == storyId, cancellationToken);
        }
    }

    public class SetStoryStatusCommandHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor, TimeProvider timeProvider)
        : IRequestHandler<SetStoryStatusCommand, ManagedStoryModel>
    {
        public async Task<ManagedStoryModel> Handle(SetStoryStatusCommand request, CancellationToken cancellationToken)
        {
            var story = await context.Stories
                .Include(s => s.Photos)
                .SingleAsync(s => s.StoryId == request.StoryId, cancellationToken);

            story.Status = Enum.Parse<StoryStatus>(request.Status, true);
            if (story.Status == StoryStatus.Published && story.PublishedAt == null)
            {
                story.PublishedAt = timeProvider.GetUtcNow();
            }

            await context.SaveChangesAsync(cancellationToken);

            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            var placements = await context.GetPlacementLookupAsync(cancellationToken, story.Photos.Select(p => p.PhotoId).ToArray());
            return story.ToManagedModel(photoStorage, tenantIdentifier, placements);
        }
    }
}
