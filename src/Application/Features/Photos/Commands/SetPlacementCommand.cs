using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Photos.Commands
{
    /// <summary>
    /// Assigns a photo to one spot on the site, or clears the spot when <see cref="PhotoId"/> is null.
    /// For a spot-first editor ("choose a photo for Home › Intro").
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record SetPlacementCommand(string Key, Guid? PhotoId) : IRequest<bool>;

    public class SetPlacementCommandValidator : AbstractValidator<SetPlacementCommand>
    {
        private readonly ElysianContext _context;
        public SetPlacementCommandValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.Key)
                .NotEmpty()
                .Matches(PhotoValidation.PlacementKeyPattern);

            RuleFor(v => v.PhotoId)
                .MustAsync(BeExistingPhoto)
                    .WithMessage("No matching photo found. The ID may be incorrect, or the photo has been deleted.")
                .When(v => v.PhotoId.HasValue);
        }

        public async Task<bool> BeExistingPhoto(Guid? photoId, CancellationToken cancellationToken)
        {
            return await _context.Photos.AnyAsync(p => p.PhotoId == photoId, cancellationToken);
        }
    }

    public class SetPlacementCommandHandler(ElysianContext context) : IRequestHandler<SetPlacementCommand, bool>
    {
        public async Task<bool> Handle(SetPlacementCommand request, CancellationToken cancellationToken)
        {
            await context.SetSpotAsync(request.Key, request.PhotoId, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
