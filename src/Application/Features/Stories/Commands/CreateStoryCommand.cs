using Elysian.Application.Features.Photos;
using Elysian.Application.Features.Stories.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using MediatR;

namespace Elysian.Application.Features.Stories.Commands
{
    /// <summary>
    /// Starts a draft story with a slug made from its title
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record CreateStoryCommand(string Category, string Title) : IRequest<ManagedStoryModel>;

    public class CreateStoryCommandValidator : AbstractValidator<CreateStoryCommand>
    {
        public CreateStoryCommandValidator()
        {
            RuleFor(v => v.Category)
                .NotEmpty()
                .Matches(PhotoValidation.CategoryPattern);

            RuleFor(v => v.Title)
                .Must(t => !string.IsNullOrWhiteSpace(t))
                    .WithMessage("Give the story a title.")
                .MaximumLength(StoryValidation.TitleMaxLength);
        }
    }

    public class CreateStoryCommandHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<CreateStoryCommand, ManagedStoryModel>
    {
        public async Task<ManagedStoryModel> Handle(CreateStoryCommand request, CancellationToken cancellationToken)
        {
            var title = request.Title.Trim();
            var story = new Story
            {
                StoryId = Guid.NewGuid(),
                Category = request.Category,
                Title = title,
                Slug = await context.MakeUniqueAsync(StorySlugs.FromTitle(title), null, cancellationToken),
                Status = StoryStatus.Draft
            };

            context.Stories.Add(story);
            await context.SaveChangesAsync(cancellationToken);

            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            return story.ToManagedModel(photoStorage, tenantIdentifier, Enumerable.Empty<string>().ToLookup(_ => Guid.Empty));
        }
    }
}
