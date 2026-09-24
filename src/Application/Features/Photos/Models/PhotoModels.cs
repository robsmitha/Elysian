using Elysian.Application.Interfaces;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.Photos.Models
{
    /// <summary>
    /// Public portfolio shape. The browser builds variant URLs as <c>{SrcBase}/{width}.webp</c>
    /// for each entry in <see cref="Widths"/>.
    /// </summary>
    public record PortfolioPhotoModel(
        Guid Id,
        string Category,
        string? Slot,
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
        string? Slot,
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
        public static string? SrcBase(this Photo photo, IPhotoStorage photoStorage, string tenantIdentifier) =>
            photo.Version > 0
                ? photoStorage.GetPublicUrl(PhotoPaths.VersionFolder(tenantIdentifier, photo.PhotoId, photo.Version))
                : null;

        public static PortfolioPhotoModel ToPortfolioModel(this Photo photo, IPhotoStorage photoStorage, string tenantIdentifier) =>
            new(photo.PhotoId, photo.Category, photo.Slot, photo.SortOrder, photo.AltText,
                photo.Width ?? 0, photo.Height ?? 0, photo.FocusX, photo.FocusY, photo.Placeholder,
                photo.SrcBase(photoStorage, tenantIdentifier)!, photo.VariantWidths);

        public static PhotoModel ToModel(this Photo photo, IPhotoStorage photoStorage, string tenantIdentifier) =>
            new(photo.PhotoId, photo.Category, photo.Slot, photo.SortOrder, photo.AltText,
                photo.Width, photo.Height, photo.FocusX, photo.FocusY, photo.Placeholder,
                photo.SrcBase(photoStorage, tenantIdentifier), photo.VariantWidths,
                photo.Status.ToString(), photo.ProcessingError, photo.OriginalFileName, photo.OriginalFileSize, photo.CreatedAt);
    }
}
