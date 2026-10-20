using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elysian.Application.Features.Placements.Models;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Placements
{
    /// <summary>
    /// Works out what every reserved slot should show right now, from the database alone. Fallback order:
    /// explicit placement → first gallery photo in the slot's category (if it has one) → site-default → neutral.
    /// Only Ready photos count, matching <c>GetPortfolioQuery</c>.
    /// </summary>
    public class PlacementResolver(ElysianContext context, PlacementCatalog catalog, IOptions<PhotoStorageSettings> photoStorageSettings)
    {
        public async Task<IReadOnlyList<ResolvedSlot>> ResolveAsync(CancellationToken cancellationToken)
        {
            // Same order as GetPortfolioQuery within a category, so "first photo" matches a site's own cover fallback
            var ready = await context.Photos.AsNoTracking()
                .Where(p => p.Status == PhotoStatus.Ready && p.Version > 0)
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
            var readyById = ready.ToDictionary(p => p.PhotoId);

            var assignments = await context.Placements.AsNoTracking()
                .Select(p => new { p.Key, p.PhotoId })
                .ToListAsync(cancellationToken);
            var photoIdByKey = assignments
                .GroupBy(a => a.Key)
                .ToDictionary(g => g.Key, g => g.First().PhotoId);

            Photo? Assigned(string key) =>
                photoIdByKey.TryGetValue(key, out var photoId) && readyById.TryGetValue(photoId, out var photo) ? photo : null;

            var siteDefault = Assigned(catalog.SiteDefaultKey);

            return catalog.Slots.Select(slot =>
            {
                // An explicit pick may be a story photo; the category fallback is the gallery's first photo,
                // and galleries never include story photos
                var (photo, source) = Assigned(slot.Key) is { } explicitPhoto
                    ? (explicitPhoto, PlacementSource.Explicit)
                    : slot.FallbackCategory != null
                        && ready.FirstOrDefault(p => p.Category == slot.FallbackCategory && p.StoryId == null) is { } categoryPhoto
                        ? (categoryPhoto, PlacementSource.Category)
                        : siteDefault != null
                            ? (siteDefault, PlacementSource.SiteDefault)
                            : ((Photo?)null, PlacementSource.Neutral);

                return new ResolvedSlot(slot, photo, source, Fingerprint(slot, photo));
            }).ToList();
        }

        /// <summary>
        /// Changes whenever the published bytes would: a different photo or version, a moved focal point,
        /// a new ratio/width/quality, or a bumped recipe. Stored on each file so unchanged slots are skipped.
        /// </summary>
        private string Fingerprint(PlacementSlot slot, Photo? photo)
        {
            var recipe = string.Join('|',
                catalog.RecipeVersion,
                slot.Ratio.ToString("R", CultureInfo.InvariantCulture),
                string.Join(',', slot.Widths),
                photoStorageSettings.Value.WebpQuality);

            var content = photo == null
                ? "neutral"
                : string.Join('|',
                    photo.PhotoId.ToString("N"),
                    photo.Version,
                    photo.FocusX.ToString("R", CultureInfo.InvariantCulture),
                    photo.FocusY.ToString("R", CultureInfo.InvariantCulture));

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{recipe}#{content}"));
            return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }
    }
}
