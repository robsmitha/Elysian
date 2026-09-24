namespace Elysian.Application.Interfaces
{
    public interface IPhotoProcessor
    {
        /// <summary>
        /// Decodes, orients and strips an original, then encodes one WebP per requested width
        /// (capped at the source width, never upscaled) plus a tiny placeholder.
        /// </summary>
        /// <exception cref="Exceptions.PhotoProcessingException">The stream is not a supported, sane image.</exception>
        Task<ProcessedPhoto> ProcessAsync(Stream original, IReadOnlyCollection<int> widths, CancellationToken cancellationToken = default);
    }

    public record ProcessedPhoto(int Width, int Height, string Placeholder, IReadOnlyList<PhotoVariant> Variants);

    public record PhotoVariant(int Width, int Height, byte[] Content, string ContentType);
}
