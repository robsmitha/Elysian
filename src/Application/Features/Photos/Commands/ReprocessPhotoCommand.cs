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
    /// Admin-triggered re-run of processing (e.g. after a failure or a settings change); writes a new version
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record ReprocessPhotoCommand(Guid PhotoId) : IRequest<PhotoModel>;

    public class ReprocessPhotoCommandValidator : AbstractValidator<ReprocessPhotoCommand>
    {
        private readonly ElysianContext _context;
        public ReprocessPhotoCommandValidator(ElysianContext context)
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

    public class ReprocessPhotoCommandHandler(ElysianContext context, IMediator mediator, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<ReprocessPhotoCommand, PhotoModel>
    {
        public async Task<PhotoModel> Handle(ReprocessPhotoCommand request, CancellationToken cancellationToken)
        {
            var originalBlobName = await context.Photos
                .Where(p => p.PhotoId == request.PhotoId)
                .Select(p => p.OriginalBlobName)
                .SingleAsync(cancellationToken);

            try
            {
                await mediator.Send(new ProcessPhotoCommand(originalBlobName), cancellationToken);
            }
            catch
            {
                // Failure is already recorded on the photo; the admin sees it in the returned model
            }

            var photo = await context.Photos.AsNoTracking().SingleAsync(p => p.PhotoId == request.PhotoId, cancellationToken);
            return photo.ToModel(photoStorage, multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!);
        }
    }
}
