using System.Text.Json;
using Elysian.Application.Features.Placements;

namespace Elysian.Application.Features.Sites
{
    /// <param name="PublicUrl">The site's primary address, e.g. https://www.example.com</param>
    public sealed record SiteInfo(string Name, string PublicUrl);

    /// <summary>
    /// A photo category on the site. <see cref="Slug"/> is stored on photos: change names freely, never slugs.
    /// </summary>
    public sealed record SiteCategory(string Slug, string Name, bool HasGallery = true, bool HasStories = false);

    /// <summary>
    /// A place on the site that shows a hand-picked photo. <see cref="Key"/> is the placement key stored in the
    /// database: change labels freely, never keys. Some spots also have a reserved image slot in
    /// <see cref="SiteManifestDocument.Placements"/>, published to a fixed address; the rest are shown from the
    /// photo itself.
    /// </summary>
    /// <param name="Label">What the admin app calls it, e.g. "Hero strip, photo 1"</param>
    /// <param name="Page">Key of the page it is on</param>
    public sealed record SiteSpot(string Key, string Label, string Page);

    /// <summary>
    /// A page that has featured spots. <see cref="Layout"/> is a wireframe of the page for the admin app to
    /// draw: a list of blocks, where a block with a "spot" property stands for that spot. Elysian only
    /// checks that the spots exist; the block vocabulary belongs to the admin app.
    /// </summary>
    public sealed record SitePage(string Key, string Name, string Path, JsonElement? Layout = null);

    /// <summary>
    /// What a site says about itself. The site's repository is the source of truth: its build compiles this
    /// same data into its pages and also writes it out as a file, and Elysian keeps a copy of that file. JSON
    /// shape: <c>{ version, site: { name, publicUrl }, categories: [...], pages: [...], spots: [...],
    /// placements: { recipeVersion, siteDefaultKey, slots: [...] } }</c>.
    /// </summary>
    public sealed class SiteManifestDocument
    {
        /// <summary>
        /// Larger than any real site needs; keeps a mistaken upload from being stored
        /// </summary>
        public const int MaxLength = 256 * 1024;

        public const int CurrentVersion = 1;

        public int Version { get; init; } = CurrentVersion;

        public SiteInfo? Site { get; init; }

        public IReadOnlyList<SiteCategory> Categories { get; init; } = [];

        public IReadOnlyList<SitePage> Pages { get; init; } = [];

        /// <summary>
        /// Every spot a photo can be picked for
        /// </summary>
        public IReadOnlyList<SiteSpot> Spots { get; init; } = [];

        /// <summary>
        /// The spots that are published as reserved images. Null for a site with none.
        /// </summary>
        public PlacementCatalog? Placements { get; init; }

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <exception cref="InvalidOperationException">The document can't be read or breaks a rule; the message says which</exception>
        public static SiteManifestDocument FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("The site manifest is empty.");
            }

            if (json.Length > MaxLength)
            {
                throw new InvalidOperationException($"The site manifest is larger than {MaxLength / 1024} KB.");
            }

            SiteManifestDocument? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<SiteManifestDocument>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"The site manifest isn't valid JSON: {ex.Message}", ex);
            }

            if (manifest == null)
            {
                throw new InvalidOperationException("The site manifest is empty.");
            }

            manifest.Validate();
            return manifest;
        }

        private void Validate()
        {
            if (Version != CurrentVersion)
            {
                throw new InvalidOperationException($"Site manifest version {Version} isn't supported; this is version {CurrentVersion}.");
            }

            if (Site == null || string.IsNullOrWhiteSpace(Site.Name))
            {
                throw new InvalidOperationException("The site manifest needs site.name.");
            }

            if (!Uri.TryCreate(Site.PublicUrl, UriKind.Absolute, out var publicUrl) || publicUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("The site manifest needs site.publicUrl as an https address.");
            }

            if (Categories.Any(c => c == null || string.IsNullOrWhiteSpace(c.Slug) || string.IsNullOrWhiteSpace(c.Name)))
            {
                throw new InvalidOperationException("Every category needs a slug and a name.");
            }

            if (Duplicate(Categories.Select(c => c.Slug)) is { } category)
            {
                throw new InvalidOperationException($"The site manifest lists category {category} twice.");
            }

            if (Pages.Any(p => p == null || string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Name)))
            {
                throw new InvalidOperationException("Every page needs a key and a name.");
            }

            if (Duplicate(Pages.Select(p => p.Key)) is { } page)
            {
                throw new InvalidOperationException($"The site manifest lists page {page} twice.");
            }

            if (Spots.Any(s => s == null || string.IsNullOrWhiteSpace(s.Key) || string.IsNullOrWhiteSpace(s.Label)))
            {
                throw new InvalidOperationException("Every spot needs a key and a label.");
            }

            if (Duplicate(Spots.Select(s => s.Key)) is { } duplicateSpot)
            {
                throw new InvalidOperationException($"The site manifest lists spot {duplicateSpot} twice.");
            }

            var pageKeys = Pages.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var spot in Spots)
            {
                if (!pageKeys.Contains(spot.Page ?? string.Empty))
                {
                    throw new InvalidOperationException($"Spot {spot.Key} is on page {spot.Page}, which the manifest doesn't list.");
                }
            }

            Placements?.Validate();

            var slots = Placements?.Slots ?? [];
            var spotKeys = Spots.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
            var categorySlugs = Categories.Select(c => c.Slug).ToHashSet(StringComparer.Ordinal);

            // A slot or a default nobody can pick a photo for would only ever show its fallback
            if (Placements != null && !spotKeys.Contains(Placements.SiteDefaultKey))
            {
                throw new InvalidOperationException($"The site default {Placements.SiteDefaultKey} isn't one of the manifest's spots.");
            }

            foreach (var slot in slots)
            {
                if (!spotKeys.Contains(slot.Key))
                {
                    throw new InvalidOperationException($"Placement slot {slot.Key} isn't one of the manifest's spots.");
                }

                // A slot that falls back to a category nobody can upload to would silently never fill
                if (slot.FallbackCategory != null && categorySlugs.Count > 0 && !categorySlugs.Contains(slot.FallbackCategory))
                {
                    throw new InvalidOperationException($"Placement slot {slot.Key} falls back to category {slot.FallbackCategory}, which the manifest doesn't list.");
                }
            }

            foreach (var layoutPage in Pages)
            {
                if (layoutPage.Layout is not { } layout || layout.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (layout.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException($"The layout of page {layoutPage.Key} must be a list of blocks.");
                }

                foreach (var spot in SpotsIn(layout))
                {
                    if (!spotKeys.Contains(spot))
                    {
                        throw new InvalidOperationException($"The layout of page {layoutPage.Key} shows spot {spot}, which the manifest doesn't list.");
                    }
                }
            }
        }

        /// <summary>
        /// Every "spot" value anywhere in a layout, however its blocks are nested
        /// </summary>
        private static IEnumerable<string> SpotsIn(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        foreach (var spot in SpotsIn(item))
                        {
                            yield return spot;
                        }
                    }
                    break;

                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("spot") && property.Value.ValueKind == JsonValueKind.String)
                        {
                            yield return property.Value.GetString()!;
                        }
                        else
                        {
                            foreach (var spot in SpotsIn(property.Value))
                            {
                                yield return spot;
                            }
                        }
                    }
                    break;
            }
        }

        private static string? Duplicate(IEnumerable<string> values) =>
            values.GroupBy(v => v, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
    }
}
