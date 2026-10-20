using System.Reflection;
using System.Text.Json;

namespace Elysian.Application.Features.Placements
{
    /// <summary>
    /// One reserved image slot on a site. <see cref="Key"/> is the placement key in the database; <see cref="Name"/>
    /// is the public file name stem. Every variant is cropped to <see cref="Ratio"/> (width / height), so a site can
    /// derive each file's URL and size without a database call.
    /// </summary>
    /// <param name="FallbackCategory">Category whose first gallery photo fills the slot when nothing is picked</param>
    public sealed record PlacementSlot(string Key, string Name, double Ratio, int[] Widths, string Alt, string? FallbackCategory)
    {
        public int LargestWidth => Widths.Max();

        /// <summary>
        /// Height of a variant. A site computing sizes itself must round the same way (half away from zero).
        /// </summary>
        public int HeightFor(int width) => (int)Math.Round(width / Ratio, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A site's reserved slots, owned by the site (like its placement keys) and handed to
    /// <c>AddPlacementFeatures</c>. JSON shape: <c>{ recipeVersion, siteDefaultKey, slots: [{ key, name, ratio,
    /// widths, alt, fallbackCategory? }] }</c>. Bump <see cref="RecipeVersion"/> when the rendering itself changes,
    /// so the next publish re-renders every slot; ratio/width/quality changes are picked up automatically.
    /// </summary>
    public sealed class PlacementCatalog
    {
        public int RecipeVersion { get; init; }

        /// <summary>
        /// Placement key of the photo used for any slot with nothing else to show
        /// </summary>
        public string SiteDefaultKey { get; init; } = string.Empty;

        public IReadOnlyList<PlacementSlot> Slots { get; init; } = [];

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public static PlacementCatalog FromJson(Stream json)
        {
            var catalog = JsonSerializer.Deserialize<PlacementCatalog>(json, JsonOptions)
                ?? throw new InvalidOperationException("The placement catalog is empty.");
            catalog.Validate();
            return catalog;
        }

        public static PlacementCatalog FromJson(string json)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            return FromJson(stream);
        }

        /// <summary>
        /// For a catalog embedded in the host, e.g. <c>&lt;EmbeddedResource Include="…" LogicalName="placement-slots.json" /&gt;</c>
        /// </summary>
        public static PlacementCatalog FromEmbeddedResource(Assembly assembly, string resourceName)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource {resourceName} is missing from {assembly.GetName().Name}.");
            return FromJson(stream);
        }

        private void Validate()
        {
            if (Slots.Count == 0 || string.IsNullOrWhiteSpace(SiteDefaultKey))
            {
                throw new InvalidOperationException("The placement catalog needs slots and a siteDefaultKey.");
            }

            foreach (var slot in Slots)
            {
                if (string.IsNullOrWhiteSpace(slot.Key) || string.IsNullOrWhiteSpace(slot.Name))
                {
                    throw new InvalidOperationException("Every placement slot needs a key and a name.");
                }

                if (slot.Ratio <= 0 || slot.Widths is not { Length: > 0 } || slot.Widths.Any(w => w <= 0))
                {
                    throw new InvalidOperationException($"Placement slot {slot.Key} needs a positive ratio and widths.");
                }
            }

            var duplicate = Slots.GroupBy(s => s.Name).FirstOrDefault(g => g.Count() > 1)?.Key
                ?? Slots.GroupBy(s => s.Key).FirstOrDefault(g => g.Count() > 1)?.Key;
            if (duplicate != null)
            {
                throw new InvalidOperationException($"The placement catalog lists {duplicate} twice.");
            }
        }
    }
}
