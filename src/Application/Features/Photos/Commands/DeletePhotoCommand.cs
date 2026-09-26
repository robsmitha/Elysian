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
    /// Soft-deletes the record, clears the spots it filled, and removes the original and every variant from storage
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoDelete)]
    public record DeletePhotoCommand(Guid PhotoId) : IRequest<bool>;

    public class DeletePhotoCommandValidator : AbstractValidator<DeletePhotoCommand>
    {
        private readonly ElysianContext _context;
        public DeletePhotoCommandValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.PhotoId)
                .NotEmpty()
                .MustAsync(BeExistingPhoto)
                    .WithMessage("No matching record found. The ID may be incorrect, or the record has been deleted.");
        }

        public async Task<bool> BeExistingPhoto(Guid photoId, CancellationToken cancellationToken)
        {
            return await _context.Photos.AnyAsync(p => p.PhotoId == photoId, cancellationToken);
        }
    }

    public class DeletePhotoCommandHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor) : IRequestHandler<DeletePhotoCommand, bool>
    {
        public async Task<bool> Handle(DeletePhotoCommand request, CancellationToken cancellationToken)
        {
            var photo = await context.Photos.SingleAsync(p => p.PhotoId == request.PhotoId, cancellationToken);
            photo.IsDeleted = true;
            await context.ClearSpotsForPhotoAsync(photo.PhotoId, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            await photoStorage.DeleteOriginalAsync(photo.OriginalBlobName, cancellationToken);
            await photoStorage.DeleteVariantsAsync(PhotoPaths.PhotoFolder(tenantIdentifier, photo.PhotoId), cancellationToken);

            return true;
        }
    }
}
