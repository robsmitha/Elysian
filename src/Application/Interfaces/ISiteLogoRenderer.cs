namespace Elysian.Application.Interfaces
{
    /// <param name="Width">Of <paramref name="Large"/>, in pixels</param>
    /// <param name="Height">Of <paramref name="Large"/>, in pixels; always even</param>
    /// <param name="Large">WebP, for screens with two pixels to a point</param>
    /// <param name="Small">WebP at exactly half the size</param>
    public sealed record SiteLogoFiles(int Width, int Height, byte[] Large, byte[] Small);

    /// <summary>
    /// Turns an uploaded logo into the two files a site shows: scaled to fit a header, never enlarged, with
    /// its transparency kept and everything else about the upload (metadata, colour profile quirks) dropped.
    /// </summary>
    public interface ISiteLogoRenderer
    {
        /// <exception cref="Exceptions.CustomValidationException">The upload isn't a PNG, JPEG or WebP image, or is too small to use</exception>
        Task<SiteLogoFiles> RenderAsync(byte[] upload, CancellationToken cancellationToken = default);
    }
}
