using Elysian.Application.Exceptions;
using Elysian.Application.Features.SiteContent;
using Elysian.Application.Interfaces;
using FluentValidation.Results;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Draws an uploaded logo again as two lossless WebP files with its transparency kept. Only PNG, JPEG and
    /// WebP are read: an SVG can carry script, and nothing here would make one safe.
    /// </summary>
    public class ImageSharpSiteLogoRenderer : ISiteLogoRenderer
    {
        /// <summary>Larger than any logo; keeps a small file that claims to be enormous from being decoded</summary>
        private const int MaxSourceSide = 6000;

        private static readonly DecoderOptions Decoder = new()
        {
            MaxFrames = 1,
            Configuration = new Configuration(new PngConfigurationModule(), new JpegConfigurationModule(), new WebpConfigurationModule()),
        };

        public async Task<SiteLogoFiles> RenderAsync(byte[] upload, CancellationToken cancellationToken = default)
        {
            static CustomValidationException Refused(string message) => new([new ValidationFailure("logo", message)]);

            ImageInfo info;
            try
            {
                info = Image.Identify(Decoder, upload);
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                throw Refused("Upload a PNG, JPEG or WebP image.");
            }
            if (info.Width > MaxSourceSide || info.Height > MaxSourceSide)
            {
                throw Refused($"That image is too large. Upload one no more than {MaxSourceSide} pixels on its longest side.");
            }

            var (width, height) = Fit(info.Width, info.Height);
            if (height < SiteLogoPaths.MinHeight)
            {
                throw Refused(info.Height < SiteLogoPaths.MinHeight
                    ? $"That image is too small. Upload one at least {SiteLogoPaths.MinHeight} pixels tall."
                    : "That image is too wide for its height to be shown as a logo. Upload one that is closer to square.");
            }

            Image<Rgba32> image;
            try
            {
                image = Image.Load<Rgba32>(Decoder, upload);
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                throw Refused("That image couldn't be read. Save it again as a PNG and upload that.");
            }

            using (image)
            {
                image.Mutate(x => x.AutoOrient());
                // Nothing of the upload but its pixels
                image.Metadata.ExifProfile = null;
                image.Metadata.XmpProfile = null;
                image.Metadata.IptcProfile = null;
                image.Metadata.IccProfile = null;

                using var large = image.Clone(x => x.Resize(width, height, KnownResamplers.Lanczos3));
                using var small = image.Clone(x => x.Resize(Math.Max(1, (int)Math.Round(width / 2.0, MidpointRounding.AwayFromZero)), height / 2, KnownResamplers.Lanczos3));
                return new SiteLogoFiles(width, height, await EncodeAsync(large, cancellationToken), await EncodeAsync(small, cancellationToken));
            }
        }

        /// <summary>
        /// The size of the larger file for an upload of this size: inside the most a header shows at two
        /// pixels to a point, never larger than the upload, and an even height so half of it is whole.
        /// </summary>
        public static (int Width, int Height) Fit(int sourceWidth, int sourceHeight)
        {
            if (sourceWidth < 1 || sourceHeight < 1)
            {
                return (0, 0);
            }
            var scale = Math.Min(1.0, Math.Min((double)SiteLogoPaths.MaxHeight / sourceHeight, (double)SiteLogoPaths.MaxWidth / sourceWidth));
            var height = (int)Math.Floor(sourceHeight * scale) / 2 * 2;
            var width = Math.Clamp((int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero), 1, SiteLogoPaths.MaxWidth);
            return (width, height);
        }

        private static async Task<byte[]> EncodeAsync(Image image, CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            // Lossless: a logo is flat colour and sharp edges, which lossy WebP smears, and it is small either way
            await image.SaveAsync(output, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless, Method = WebpEncodingMethod.BestQuality }, cancellationToken);
            return output.ToArray();
        }
    }
}
