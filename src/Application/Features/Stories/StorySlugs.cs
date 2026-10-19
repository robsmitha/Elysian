using System.Globalization;
using System.Text;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Stories
{
    public static class StorySlugs
    {
        /// <summary>
        /// "Emily &amp; James" at "Goodwood Museum" → "emily-and-james-goodwood-museum"
        /// </summary>
        public static string FromTitle(string title, string? venueName = null)
        {
            var source = string.IsNullOrWhiteSpace(venueName) ? title : $"{title} {venueName}";
            source = source.Replace("&", " and ").Replace("+", " and ");

            // Drop accents (é → e) before keeping only ASCII letters and digits
            var normalized = source.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            var pendingHyphen = false;
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c == '\'' || c == '’')
                {
                    continue;
                }

                var lower = char.ToLowerInvariant(c);
                if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
                {
                    if (pendingHyphen && builder.Length > 0)
                    {
                        builder.Append('-');
                    }
                    builder.Append(lower);
                    pendingHyphen = false;
                }
                else
                {
                    pendingHyphen = true;
                }
            }

            var slug = builder.ToString();
            if (slug.Length > 100)
            {
                slug = slug[..100].TrimEnd('-');
            }
            return slug.Length > 0 ? slug : "story";
        }

        /// <summary>
        /// <paramref name="slug"/>, or it with "-2", "-3"... appended when another story already uses it
        /// </summary>
        public static async Task<string> MakeUniqueAsync(this ElysianContext context, string slug, Guid? exceptStoryId,
            CancellationToken cancellationToken)
        {
            var taken = await context.Stories
                .Where(s => s.StoryId != exceptStoryId && (s.Slug == slug || s.Slug.StartsWith(slug + "-")))
                .Select(s => s.Slug)
                .ToListAsync(cancellationToken);

            if (!taken.Contains(slug))
            {
                return slug;
            }

            for (var n = 2; ; n++)
            {
                var candidate = $"{slug}-{n}";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
