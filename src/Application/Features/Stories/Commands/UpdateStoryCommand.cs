using Elysian.Application.Features.Photos;
using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Features.Stories.Models;
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
    /// Saves every editable field. Blank optional fields are stored as null so the page hides them.
    /// Changing the category moves the story's photos with it.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record UpdateStoryCommand(
        Guid StoryId,
        string Slug,
        string Category,
        string Title,
        string? Description,
        DateOnly? EventDate,
        string? VenueName,
        string? City,
        string? State,
        Guid? HeroPhotoId,
        string? SeoTitle,
        string? SeoDescription) : IRequest<ManagedStoryModel>;

    public class UpdateStoryCommandValidator : AbstractValidator<UpdateStoryCommand>
    {
        private readonly ElysianContext _context;
        public UpdateStoryCommandValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.StoryId)
                .NotEmpty()
                .MustAsync(BeExistingStory)
                    .WithMessage("No matching story found. The ID may be incorrect, or the story has been deleted.");

            RuleFor(v => v.Slug)
                .NotEmpty()
                .MaximumLength(StoryValidation.SlugMaxLength)
                .Matches(StoryValidation.SlugPattern)
                    .WithMessage("Use lowercase letters, numbers and single hyphens, like \"emily-and-james\".");

            RuleFor(v => v)
                .MustAsync(HaveUnusedSlug)
                    .WithName(nameof(UpdateStoryCommand.Slug))
                    .WithMessage("Another story already uses this URL.")
                .When(v => !string.IsNullOrEmpty(v.Slug));

            RuleFor(v => v.Category)
                .NotEmpty()
                .Matches(PhotoValidation.CategoryPattern);

            RuleFor(v => v.Title)
                .Must(t => !string.IsNullOrWhiteSpace(t))
                    .WithMessage("Give the story a title.")
                .MaximumLength(StoryValidation.TitleMaxLength);

            RuleFor(v => v.Description).MaximumLength(StoryValidation.DescriptionMaxLength);
            RuleFor(v => v.VenueName).MaximumLength(StoryValidation.VenueNameMaxLength);
            RuleFor(v => v.City).MaximumLength(StoryValidation.CityMaxLength);
            RuleFor(v => v.State).MaximumLength(StoryValidation.StateMaxLength);
            RuleFor(v => v.SeoTitle).MaximumLength(StoryValidation.SeoTitleMaxLength);
            RuleFor(v => v.SeoDescription).MaximumLength(StoryValidation.SeoDescriptionMaxLength);

            RuleFor(v => v)
                .MustAsync(HaveHeroFromThisStory)
                    .WithName(nameof(UpdateStoryCommand.HeroPhotoId))
                    .WithMessage("The hero photo must be one of this story's photos.")
                .When(v => v.HeroPhotoId.HasValue);
        }

        public async Task<bool> BeExistingStory(Guid storyId, CancellationToken cancellationToken)
        {
            return await _context.Stories.AnyAsync(s => s.StoryId == storyId, cancellationToken);
        }

        public async Task<bool> HaveUnusedSlug(UpdateStoryCommand command, CancellationToken cancellationToken)
        {
            return !await _context.Stories.AnyAsync(s => s.Slug == command.Slug && s.StoryId != command.StoryId, cancellationToken);
        }

        public async Task<bool> HaveHeroFromThisStory(UpdateStoryCommand command, CancellationToken cancellationToken)
        {
            return await _context.Photos.AnyAsync(p => p.PhotoId == command.HeroPhotoId && p.StoryId == command.StoryId, cancellationToken);
        }
    }

    public class UpdateStoryCommandHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<UpdateStoryCommand, ManagedStoryModel>
    {
        public async Task<ManagedStoryModel> Handle(UpdateStoryCommand request, CancellationToken cancellationToken)
        {
            var story = await context.Stories
                .Include(s => s.Photos)
                .SingleAsync(s => s.StoryId == request.StoryId, cancellationToken);

            if (story.Category != request.Category)
            {
                story.Photos.ForEach(p => p.Category = request.Category);
            }

            story.Slug = request.Slug;
            story.Category = request.Category;
            story.Title = request.Title.Trim();
            story.Description = Clean(request.Description);
            story.EventDate = request.EventDate;
            story.VenueName = Clean(request.VenueName);
            story.City = Clean(request.City);
            story.State = Clean(request.State);
            story.HeroPhotoId = request.HeroPhotoId;
            story.SeoTitle = Clean(request.SeoTitle);
            story.SeoDescription = Clean(request.SeoDescription);

            await context.SaveChangesAsync(cancellationToken);

            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            var placements = await context.GetPlacementLookupAsync(cancellationToken, story.Photos.Select(p => p.PhotoId).ToArray());
            return story.ToManagedModel(photoStorage, tenantIdentifier, placements);
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
