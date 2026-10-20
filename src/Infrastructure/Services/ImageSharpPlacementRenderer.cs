using Elysian.Application.Features.Placements;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Crops and encodes reserved slots with the same WebP settings as <see cref="ImageSharpPhotoProcessor"/>.
    /// The difference: each variant is cropped to the slot's ratio around the admin's focal point.
    /// </summary>
    public class ImageSharpPlacementRenderer(IOptions<PhotoStorageSettings> photoStorageSettings) : IPlacementRenderer
    {
        /// <summary>
        /// A soft ivory, close to a typical photo frame background, so an empty slot looks deliberate
        /// </summary>
        private static readonly Rgb24 NeutralColor = new(0xF8, 0xF2, 0xEF);

        private readonly PhotoStorageSettings _settings = photoStorageSettings.Value;

        public async Task<IPlacementSource> LoadAsync(Stream original, CancellationToken cancellationToken = default)
        {
            var image = await Image.LoadAsync<Rgb24>(new DecoderOptions { MaxFrames = 1 }, original, cancellationToken);
            image.Mutate(x => x.AutoOrient());

            // Same as the photo pipeline: drop EXIF (GPS, serials), XMP and IPTC; keep the ICC profile for color
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;
            foreach (var frame in image.Frames)
            {
                frame.Metadata.ExifProfile = null;
                frame.Metadata.XmpProfile = null;
                frame.Metadata.IptcProfile = null;
            }

            return new Source(image, this);
        }

        public async Task<IReadOnlyList<PlacementVariant>> RenderNeutralAsync(PlacementSlot slot, CancellationToken cancellationToken = default)
        {
            var variants = new List<PlacementVariant>(slot.Widths.Length);
            foreach (var width in slot.Widths.Order())
            {
                var height = slot.HeightFor(width);
                using var blank = new Image<Rgb24>(width, height, NeutralColor);
                variants.Add(new PlacementVariant(width, width, height, await EncodeAsync(blank, cancellationToken)));
            }

            return variants;
        }

        /// <summary>
        /// Largest rectangle of the given ratio, centred on the focal point and clamped inside the image. Keeping the
        /// subject near the centre also means a page's own CSS re-crop (e.g. a min-height) still keeps it in frame.
        /// </summary>
        public static Rectangle CropFor(int width, int height, double ratio, double focusX, double focusY)
        {
            if ((double)width / height > ratio)
            {
                var cropWidth = Math.Clamp((int)Math.Round(height * ratio), 1, width);
                var x = Math.Clamp((int)Math.Round(focusX * width - cropWidth / 2.0), 0, width - cropWidth);
                return new Rectangle(x, 0, cropWidth, height);
            }

            var cropHeight = Math.Clamp((int)Math.Round(width / ratio), 1, height);
            var y = Math.Clamp((int)Math.Round(focusY * height - cropHeight / 2.0), 0, height - cropHeight);
            return new Rectangle(0, y, width, cropHeight);
        }

        private async Task<byte[]> EncodeAsync(Image image, CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            await image.SaveAsync(output, new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = _settings.WebpQuality,
                Method = WebpEncodingMethod.BestQuality,
            }, cancellationToken);
            return output.ToArray();
        }

        private sealed class Source(Image<Rgb24> image, ImageSharpPlacementRenderer renderer) : IPlacementSource
        {
            public async Task<IReadOnlyList<PlacementVariant>> RenderAsync(PlacementSlot slot, double focusX, double focusY,
                CancellationToken cancellationToken = default)
            {
                using var cropped = image.Clone(x => x.Crop(CropFor(image.Width, image.Height, slot.Ratio, focusX, focusY)));

                var variants = new List<PlacementVariant>(slot.Widths.Length);
                foreach (var namedWidth in slot.Widths.Order())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Never upscale: a narrow source is written at its own width under the same name, same ratio
                    var width = Math.Min(namedWidth, cropped.Width);
                    var height = slot.HeightFor(width);

                    using var resized = cropped.Clone(x => x.Resize(new ResizeOptions
                    {
                        // The crop already has the slot's ratio; Stretch only absorbs a pixel of rounding
                        Size = new Size(width, height),
                        Mode = ResizeMode.Stretch,
                        Sampler = KnownResamplers.Lanczos3,
                    }));

                    variants.Add(new PlacementVariant(namedWidth, width, height, await renderer.EncodeAsync(resized, cancellationToken)));
                }

                return variants;
            }

            public void Dispose() => image.Dispose();
        }
    }
}
