using Elysian.Application.Exceptions;
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
    public class ImageSharpPhotoProcessor(IOptions<PhotoStorageSettings> photoStorageSettings) : IPhotoProcessor
    {
        private const string WebpContentType = "image/webp";

        private readonly PhotoStorageSettings _settings = photoStorageSettings.Value;

        public async Task<ProcessedPhoto> ProcessAsync(Stream original, IReadOnlyCollection<int> widths, CancellationToken cancellationToken = default)
        {
            // Identify and decode both need to read from the start
            var stream = original.CanSeek ? original : await CopyToMemoryAsync(original, cancellationToken);

            ImageInfo info;
            try
            {
                info = await Image.IdentifyAsync(stream, cancellationToken);
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                throw new PhotoProcessingException("The file is not a supported image.", ex);
            }

            if ((long)info.Width * info.Height > _settings.MaxPixels)
            {
                throw new PhotoProcessingException($"The image is {info.Width}x{info.Height}, which exceeds the {_settings.MaxPixels:N0} pixel limit.");
            }

            stream.Position = 0;

            Image<Rgb24> image;
            try
            {
                image = await Image.LoadAsync<Rgb24>(new DecoderOptions { MaxFrames = 1 }, stream, cancellationToken);
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                throw new PhotoProcessingException("The image could not be decoded.", ex);
            }

            using (image)
            {
                image.Mutate(x => x.AutoOrient());
                StripPrivateMetadata(image);

                var encoder = new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = _settings.WebpQuality,
                    Method = WebpEncodingMethod.BestQuality
                };

                // Never upscale: widths past the source collapse into one native-width variant
                var targets = widths
                    .Select(w => Math.Min(w, image.Width))
                    .Distinct()
                    .OrderByDescending(w => w)
                    .ToList();

                var variants = new List<PhotoVariant>(targets.Count);

                // Step down from the largest variant rather than resampling the full original every time
                Image<Rgb24> source = image;
                foreach (var width in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var resized = width == source.Width ? source : source.Clone(x => Resize(x, width));
                    variants.Add(new PhotoVariant(resized.Width, resized.Height, await EncodeAsync(resized, encoder, cancellationToken), WebpContentType));

                    if (!ReferenceEquals(source, image) && !ReferenceEquals(source, resized))
                    {
                        source.Dispose();
                    }
                    source = resized;
                }

                var placeholder = await CreatePlaceholderAsync(source, cancellationToken);

                if (!ReferenceEquals(source, image))
                {
                    source.Dispose();
                }

                return new ProcessedPhoto(image.Width, image.Height, placeholder, variants);
            }
        }

        private async Task<string> CreatePlaceholderAsync(Image<Rgb24> source, CancellationToken cancellationToken)
        {
            using var tiny = source.Clone(x => Resize(x, Math.Min(_settings.PlaceholderWidth, source.Width)));

            // A few hundred bytes inlined in JSON; the color profile would be bigger than the pixels
            tiny.Metadata.IccProfile = null;

            var bytes = await EncodeAsync(tiny, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = 40 }, cancellationToken);
            return $"data:{WebpContentType};base64,{Convert.ToBase64String(bytes)}";
        }

        private static IImageProcessingContext Resize(IImageProcessingContext context, int width) =>
            context.Resize(new ResizeOptions
            {
                // Height 0 preserves aspect ratio
                Size = new Size(width, 0),
                Sampler = KnownResamplers.Lanczos3
            });

        /// <summary>
        /// Drops EXIF (GPS, camera serials), XMP and IPTC. The ICC profile is kept on purpose so
        /// wide-gamut exports (Adobe RGB, Display P3) render with correct color in the browser.
        /// </summary>
        private static void StripPrivateMetadata(Image image)
        {
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;

            foreach (var frame in image.Frames)
            {
                frame.Metadata.ExifProfile = null;
                frame.Metadata.XmpProfile = null;
                frame.Metadata.IptcProfile = null;
            }
        }

        private static async Task<byte[]> EncodeAsync(Image image, WebpEncoder encoder, CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            await image.SaveAsync(output, encoder, cancellationToken);
            return output.ToArray();
        }

        private static async Task<MemoryStream> CopyToMemoryAsync(Stream stream, CancellationToken cancellationToken)
        {
            var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            return memory;
        }
    }
}
