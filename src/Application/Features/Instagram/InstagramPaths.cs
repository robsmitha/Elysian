using System.Text.RegularExpressions;

namespace Elysian.Application.Features.Instagram
{
    /// <summary>
    /// Instagram thumbnails live in the public photos container beside the portfolio variants, under
    /// {tenant}/instagram/{mediaId}/{width}.webp. Media ids never change, so the files are immutable.
    /// </summary>
    public static partial class InstagramPaths
    {
        /// <summary>
        /// Base the browser builds variant URLs from as {folder}/{width}.webp
        /// </summary>
        public static string Folder(string tenantIdentifier, string mediaId) =>
            $"{tenantIdentifier}/instagram/{mediaId}";

        public static string Variant(string tenantIdentifier, string mediaId, int width) =>
            $"{Folder(tenantIdentifier, mediaId)}/{width}.webp";

        /// <summary>
        /// Prefix for deleting every variant of one post (trailing slash so 123 never matches 1234)
        /// </summary>
        public static string Prefix(string tenantIdentifier, string mediaId) =>
            $"{Folder(tenantIdentifier, mediaId)}/";

        /// <summary>
        /// Media ids become blob paths, so only accept what Instagram actually issues
        /// </summary>
        public static bool IsValidMediaId(string mediaId) => MediaIdPattern().IsMatch(mediaId);

        [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
        private static partial Regex MediaIdPattern();
    }
}
