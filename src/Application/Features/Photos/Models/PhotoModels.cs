using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Photos.Models
{
    /// <summary>
    /// Public portfolio shape. The browser builds variant URLs as <c>{SrcBase}/{width}.webp</c>
    /// for each entry in <see cref="Widths"/>.
    /// </summary>
    public record PortfolioPhotoModel(
        Guid Id,
        string Category,
        List<string> Placements,
        int SortOrder,
        string? AltText,
        int Width,
        int Height,
        double FocusX,
        double FocusY,
        string? Placeholder,
        string SrcBase,
        List<int> Widths);

    public record PhotoModel(
        Guid Id,
        string Category,
        List<string> Placements,
        int SortOrder,
        string? AltText,
        int? Width,
        int? Height,
        double FocusX,
        double FocusY,
        string? Placeholder,
        string? SrcBase,
        List<int> Widths,
        string Status,
        string? ProcessingError,
        string OriginalFileName,
        long OriginalFileSize,
        DateTimeOffset CreatedAt);

    public static class PhotoModelMappings
    {
        /// <summary>
        /// Spot keys per photo, for filling <see cref="PhotoModel.Placements"/>
        /// </summary>
        public static async Task<ILookup<Guid, string>> GetPlacementLookupAsync(this ElysianContext context,
            CancellationToken cancellationToken, params Guid[] photoIds)
        {
            var query = context.Placements.AsNoTracking();
            if (photoIds.Length > 0)
            {
                query = query.Where(p => photoIds.Contains(p.PhotoId));
            }

            var rows = await query.Select(p => new { p.PhotoId, p.Key }).ToListAsync(cancellationToken);
            return rows.ToLookup(r => r.PhotoId, r => r.Key);
        }

        public static string? SrcBase(this Photo photo, IPhotoStorage photoStorage, string tenantIdentifier) =>
            photo.Version > 0
                ? photoStorage.GetPublicUrl(PhotoPaths.VersionFolder(tenantIdentifier, photo.PhotoId, photo.Version))
                : null;

        public static PortfolioPhotoModel ToPortfolioModel(this Photo photo, IPhotoStorage photoStorage, string tenantIdentifier,
            ILookup<Guid, string> placements) =>
            new(photo.PhotoId, photo.Category, placements[photo.PhotoId].Order().ToList(), photo.SortOrder, photo.AltText,
                photo.Width ?? 0, photo.Height ?? 0, photo.FocusX, photo.FocusY, photo.Placeholder,
                photo.SrcBase(photoStorage, tenantIdentifier)!, photo.VariantWidths);

        public static PhotoModel ToModel(this Photo photo, IPhotoStorage photoStorage, string tenantIdentifier,
            ILookup<Guid, string> placements) =>
            new(photo.PhotoId, photo.Category, placements[photo.PhotoId].Order().ToList(), photo.SortOrder, photo.AltText,
                photo.Width, photo.Height, photo.FocusX, photo.FocusY, photo.Placeholder,
                photo.SrcBase(photoStorage, tenantIdentifier), photo.VariantWidths,
                photo.Status.ToString(), photo.ProcessingError, photo.OriginalFileName, photo.OriginalFileSize, photo.CreatedAt);
    }
}
