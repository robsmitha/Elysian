using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Photos.Commands
{
    /// <summary>
    /// Updates editable metadata. When <see cref="Placements"/> is given it becomes the complete set of
    /// spots this photo fills; spots another photo filled move to this one. Null leaves placements unchanged.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record UpdatePhotoCommand(Guid PhotoId, string Category, string? AltText,
        double FocusX, double FocusY, List<string>? Placements = null) : IRequest<PhotoModel>;

    public class UpdatePhotoCommandValidator : AbstractValidator<UpdatePhotoCommand>
    {
        private readonly ElysianContext _context;
        public UpdatePhotoCommandValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.PhotoId)
                .NotEmpty()
                .MustAsync(BeExistingPhoto)
                    .WithMessage("No matching record found. The ID may be incorrect, or the record has been deleted.");

            RuleFor(v => v.Category)
                .NotEmpty()
                .Matches(PhotoValidation.CategoryPattern);

            RuleFor(v => v.AltText).MaximumLength(512);
            RuleFor(v => v.FocusX).InclusiveBetween(0, 1);
            RuleFor(v => v.FocusY).InclusiveBetween(0, 1);

            RuleFor(v => v.Placements!.Count)
                .LessThanOrEqualTo(50)
                .When(v => v.Placements != null);
            RuleForEach(v => v.Placements)
                .NotEmpty()
                .Matches(PhotoValidation.PlacementKeyPattern);
        }

        public async Task<bool> BeExistingPhoto(Guid photoId, CancellationToken cancellationToken)
        {
            return await _context.Photos.AnyAsync(p => p.PhotoId == photoId, cancellationToken);
        }
    }

    public class UpdatePhotoCommandHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<UpdatePhotoCommand, PhotoModel>
    {
        public async Task<PhotoModel> Handle(UpdatePhotoCommand request, CancellationToken cancellationToken)
        {
            var photo = await context.Photos.SingleAsync(p => p.PhotoId == request.PhotoId, cancellationToken);

            if (photo.Category != request.Category)
            {
                var lastSortOrder = await context.Photos
                    .Where(p => p.Category == request.Category)
                    .MaxAsync(p => (int?)p.SortOrder, cancellationToken);
                photo.SortOrder = (lastSortOrder ?? -1) + 1;
            }

            photo.Category = request.Category;
            photo.AltText = string.IsNullOrWhiteSpace(request.AltText) ? null : request.AltText.Trim();
            photo.FocusX = request.FocusX;
            photo.FocusY = request.FocusY;

            if (request.Placements != null)
            {
                await context.SetSpotsForPhotoAsync(photo.PhotoId, request.Placements, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);

            var placements = await context.GetPlacementLookupAsync(cancellationToken, photo.PhotoId);
            return photo.ToModel(photoStorage, multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!, placements);
        }
    }
}
