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
    /// Updates editable metadata. Assigning a slot another photo holds moves the slot to this photo.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record UpdatePhotoCommand(Guid PhotoId, string Category, string? Slot, string? AltText,
        double FocusX, double FocusY) : IRequest<PhotoModel>;

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

            RuleFor(v => v.Slot)
                .Matches(PhotoValidation.SlotPattern)
                .When(v => !string.IsNullOrEmpty(v.Slot));

            RuleFor(v => v.AltText).MaximumLength(512);
            RuleFor(v => v.FocusX).InclusiveBetween(0, 1);
            RuleFor(v => v.FocusY).InclusiveBetween(0, 1);
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
            var slot = string.IsNullOrWhiteSpace(request.Slot) ? null : request.Slot.Trim();

            if (slot != null && slot != photo.Slot)
            {
                var holder = await context.Photos.SingleOrDefaultAsync(p => p.Slot == slot, cancellationToken);
                if (holder != null)
                {
                    holder.Slot = null;
                    // Release the unique slot before claiming it
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            if (photo.Category != request.Category)
            {
                var lastSortOrder = await context.Photos
                    .Where(p => p.Category == request.Category)
                    .MaxAsync(p => (int?)p.SortOrder, cancellationToken);
                photo.SortOrder = (lastSortOrder ?? -1) + 1;
            }

            photo.Category = request.Category;
            photo.Slot = slot;
            photo.AltText = string.IsNullOrWhiteSpace(request.AltText) ? null : request.AltText.Trim();
            photo.FocusX = request.FocusX;
            photo.FocusY = request.FocusY;
            await context.SaveChangesAsync(cancellationToken);

            return photo.ToModel(photoStorage, multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!);
        }
    }
}
