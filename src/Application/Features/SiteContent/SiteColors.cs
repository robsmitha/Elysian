using System.Globalization;

namespace Elysian.Application.Features.SiteContent
{
    /// <summary>
    /// Colours as a site's content may state them, and whether text in one reads on another. Only "#rrggbb" is
    /// a colour here: the value ends up in a stylesheet served to visitors, so nothing that could be anything
    /// other than a colour is accepted.
    /// </summary>
    public static class SiteColors
    {
        /// <summary>
        /// WCAG AA for body text
        /// </summary>
        public const double TextContrast = 4.5;

        /// <summary>
        /// WCAG AA for large text and for the edge of a control against what is behind it
        /// </summary>
        public const double LargeContrast = 3.0;

        public static bool IsHex(string? value) => TryParse(value, out _);

        /// <summary>
        /// Lowercase "#rrggbb", or null when the value isn't one
        /// </summary>
        public static string? Normalize(string? value) =>
            TryParse(value, out _) ? value!.Trim().ToLowerInvariant() : null;

        /// <summary>
        /// The WCAG contrast ratio between two colours, from 1 (the same) to 21 (black on white)
        /// </summary>
        public static double Contrast(string first, string second)
        {
            if (!TryParse(first, out var a) || !TryParse(second, out var b))
            {
                throw new ArgumentException("Both colours must be #rrggbb.");
            }

            var lighter = Math.Max(Luminance(a), Luminance(b));
            var darker = Math.Min(Luminance(a), Luminance(b));
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static bool TryParse(string? value, out (int R, int G, int B) color)
        {
            color = default;
            var text = value?.Trim();
            if (text is not { Length: 7 } || text[0] != '#' || !text.AsSpan(1).ContainsOnlyHex())
            {
                return false;
            }

            color = (
                int.Parse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return true;
        }

        private static bool ContainsOnlyHex(this ReadOnlySpan<char> text)
        {
            foreach (var c in text)
            {
                if (!char.IsAsciiHexDigit(c))
                {
                    return false;
                }
            }
            return true;
        }

        private static double Luminance((int R, int G, int B) color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

        private static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }
}
