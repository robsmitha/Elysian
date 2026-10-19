using Elysian.Application.Features.Photos.Commands;
using Elysian.Application.Interfaces;
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
    /// Soft-deletes the story and every photo in it, and removes those photos' files from storage
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoDelete)]
    public record DeleteStoryCommand(Guid StoryId) : IRequest<bool>;

    public class DeleteStoryCommandValidator : AbstractValidator<DeleteStoryCommand>
    {
        private readonly ElysianContext _context;
        public DeleteStoryCommandValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.StoryId)
                .NotEmpty()
                .MustAsync(BeExistingStory)
                    .WithMessage("No matching story found. The ID may be incorrect, or the story has been deleted.");
        }

        public async Task<bool> BeExistingStory(Guid storyId, CancellationToken cancellationToken)
        {
            return await _context.Stories.AnyAsync(s => s.StoryId == storyId, cancellationToken);
        }
    }

    public class DeleteStoryCommandHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor) : IRequestHandler<DeleteStoryCommand, bool>
    {
        public async Task<bool> Handle(DeleteStoryCommand request, CancellationToken cancellationToken)
        {
            var story = await context.Stories
                .Include(s => s.Photos)
                .SingleAsync(s => s.StoryId == request.StoryId, cancellationToken);

            var photos = story.Photos.ToList();
            story.HeroPhotoId = null;
            story.IsDeleted = true;
            await context.SoftDeletePhotosAsync(photos, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            await photoStorage.DeletePhotoFilesAsync(photos, tenantIdentifier, cancellationToken);

            return true;
        }
    }
}
