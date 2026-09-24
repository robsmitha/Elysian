using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Photos.Commands
{
    /// <summary>
    /// Generates the WebP variants and placeholder for an uploaded original. Invoked by the
    /// storage blob-created trigger (no user principal), so it carries no [Authorize]; the caller
    /// must have set the tenant context from the blob name first.
    /// </summary>
    public record ProcessPhotoCommand(string OriginalBlobName) : IRequest<PhotoStatus?>;

    public class ProcessPhotoCommandHandler(ElysianContext context, IPhotoStorage photoStorage, IPhotoProcessor photoProcessor,
        IOptions<PhotoStorageSettings> photoStorageSettings, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        ILogger<ProcessPhotoCommandHandler> logger)
        : IRequestHandler<ProcessPhotoCommand, PhotoStatus?>
    {
        public async Task<PhotoStatus?> Handle(ProcessPhotoCommand request, CancellationToken cancellationToken)
        {
            var photo = await context.Photos.SingleOrDefaultAsync(p => p.OriginalBlobName == request.OriginalBlobName, cancellationToken);
            if (photo == null)
            {
                // Deleted before processing, or written without going through CreatePhotoUploadCommand
                logger.LogWarning("No photo record for original {BlobName}; skipping", request.OriginalBlobName);
                return null;
            }

            photo.Status = PhotoStatus.Processing;
            photo.ProcessingError = null;
            await context.SaveChangesAsync(cancellationToken);

            try
            {
                ProcessedPhoto processed;
                await using (var original = await photoStorage.OpenOriginalAsync(photo.OriginalBlobName, cancellationToken))
                {
                    processed = await photoProcessor.ProcessAsync(original, photoStorageSettings.Value.VariantWidths, cancellationToken);
                }

                // New version folder every run so no cached immutable URL is ever overwritten
                var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
                var version = photo.Version + 1;
                foreach (var variant in processed.Variants)
                {
                    await photoStorage.UploadVariantAsync(PhotoPaths.Variant(tenantIdentifier, photo.PhotoId, version, variant.Width),
                        variant.Content, variant.ContentType, cancellationToken);
                }

                photo.Width = processed.Width;
                photo.Height = processed.Height;
                photo.Placeholder = processed.Placeholder;
                photo.VariantWidths = processed.Variants.Select(v => v.Width).OrderBy(w => w).ToList();
                photo.Version = version;
                photo.Status = PhotoStatus.Ready;
                await context.SaveChangesAsync(cancellationToken);

                return photo.Status;
            }
            catch (PhotoProcessingException ex)
            {
                // Bad input: record it for the admin UI and don't let the trigger retry
                logger.LogWarning(ex, "Photo {PhotoId} could not be processed", photo.PhotoId);
                await MarkFailedAsync(photo, ex.Message);
                return photo.Status;
            }
            catch (Exception ex)
            {
                // Transient (storage/network): record it, then rethrow so the trigger retries
                await MarkFailedAsync(photo, "Processing failed unexpectedly. It will be retried automatically; use Reprocess if it stays failed.");
                logger.LogError(ex, "Photo {PhotoId} processing failed", photo.PhotoId);
                throw;
            }
        }

        private async Task MarkFailedAsync(Photo photo, string error)
        {
            photo.Status = PhotoStatus.Failed;
            photo.ProcessingError = error;
            await context.SaveChangesAsync(CancellationToken.None);
        }
    }
}
