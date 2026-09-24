using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Photos.Commands
{
    /// <summary>
    /// Creates the photo record and returns a short-lived URL the browser PUTs the original to.
    /// Processing starts from the storage blob-created event, not from this request.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record CreatePhotoUploadCommand(string FileName, long FileSize, string Category) : IRequest<CreatePhotoUploadResponse>;

    public record CreatePhotoUploadResponse(Guid PhotoId, string UploadUrl);

    public class CreatePhotoUploadCommandValidator : AbstractValidator<CreatePhotoUploadCommand>
    {
        public CreatePhotoUploadCommandValidator(IOptions<PhotoStorageSettings> photoStorageSettings)
        {
            RuleFor(v => v.FileName)
                .NotEmpty()
                .MaximumLength(256)
                .Must(f => PhotoPaths.AllowedExtensions.Contains(Path.GetExtension(f ?? string.Empty).ToLowerInvariant()))
                    .WithMessage($"Only {string.Join(", ", PhotoPaths.AllowedExtensions)} files can be uploaded.");

            RuleFor(v => v.FileSize)
                .GreaterThan(0)
                .LessThanOrEqualTo(photoStorageSettings.Value.MaxUploadBytes)
                    .WithMessage($"Files must be {photoStorageSettings.Value.MaxUploadBytes / 1024 / 1024} MB or smaller.");

            RuleFor(v => v.Category)
                .NotEmpty()
                .Matches(PhotoValidation.CategoryPattern);
        }
    }

    public class CreatePhotoUploadCommandHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<CreatePhotoUploadCommand, CreatePhotoUploadResponse>
    {
        public async Task<CreatePhotoUploadResponse> Handle(CreatePhotoUploadCommand request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            var photoId = Guid.NewGuid();
            var blobName = PhotoPaths.Original(tenantIdentifier, photoId, request.FileName);

            var lastSortOrder = await context.Photos
                .Where(p => p.Category == request.Category)
                .MaxAsync(p => (int?)p.SortOrder, cancellationToken);

            context.Photos.Add(new Photo
            {
                PhotoId = photoId,
                Category = request.Category,
                SortOrder = (lastSortOrder ?? -1) + 1,
                OriginalFileName = Path.GetFileName(request.FileName),
                OriginalBlobName = blobName,
                OriginalFileSize = request.FileSize,
                Status = PhotoStatus.Uploaded
            });
            await context.SaveChangesAsync(cancellationToken);

            var uploadUri = await photoStorage.CreateOriginalUploadUriAsync(blobName, cancellationToken);

            return new CreatePhotoUploadResponse(photoId, uploadUri.ToString());
        }
    }
}
