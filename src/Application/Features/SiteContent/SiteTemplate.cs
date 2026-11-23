using System.Text.Json;
using System.Text.RegularExpressions;

namespace Elysian.Application.Features.SiteContent
{
    /// <summary>
    /// A set of colours an owner can choose. <see cref="OnAccent"/> is the colour of text placed on the accent,
    /// e.g. a button's label.
    /// </summary>
    public sealed record SiteTemplatePalette(string Key, string Name, string Background, string Surface, string Text, string Muted,
        string Accent, string OnAccent);

    /// <summary>
    /// A heading and body font pairing the template ships with. Only the key travels; the template knows the fonts.
    /// </summary>
    public sealed record SiteTemplateFont(string Key, string Name);

    /// <summary>
    /// One line or block of wording an owner can change
    /// </summary>
    /// <param name="Key">Stable name the template reads it by, e.g. "home.heading"</param>
    /// <param name="Group">The heading it is edited under, e.g. "Home"</param>
    /// <param name="Type">"text" (one line) or "multiline"</param>
    /// <param name="Default">What the site shows until the owner writes their own</param>
    public sealed record SiteTemplateField(string Key, string Label, string Group, string Type = SiteTemplateFieldTypes.Text,
        int MaxLength = 120, bool Required = false, string? Default = null);

    public static class SiteTemplateFieldTypes
    {
        public const string Text = "text";
        public const string Multiline = "multiline";
        public static readonly IReadOnlyList<string> All = [Text, Multiline];
    }

    /// <summary>
    /// A photo category the template has pages for. The slug is in public addresses and storage paths and never
    /// changes; an owner can rename a category and switch it off.
    /// </summary>
    public sealed record SiteTemplateCategory(string Slug, string Name);

    /// <summary>
    /// What one version of a shared template lets a site's owner change: which palettes and fonts exist, which
    /// lines of wording, which links and which categories. It is the template's own file (template.schema.json),
    /// handed to Elysian by the host; the admin app builds its editing screen from the same document, and
    /// everything an owner saves is checked against it. A breaking change is a new version.
    /// </summary>
    public sealed partial class SiteTemplate
    {
        public const int MaxLength = 128 * 1024;

        public string Key { get; init; } = string.Empty;

        public int Version { get; init; }

        public string Name { get; init; } = string.Empty;

        public IReadOnlyList<SiteTemplatePalette> Palettes { get; init; } = [];

        /// <summary>
        /// Whether an owner may replace a palette's accent with a colour of their own
        /// </summary>
        public bool AllowCustomAccent { get; init; }

        public IReadOnlyList<SiteTemplateFont> Fonts { get; init; } = [];

        public IReadOnlyList<SiteTemplateField> Fields { get; init; } = [];

        /// <summary>
        /// Names of the outside links the template shows, e.g. "instagram"
        /// </summary>
        public IReadOnlyList<string> Links { get; init; } = [];

        public IReadOnlyList<SiteTemplateCategory> Categories { get; init; } = [];

        internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$")]
        private static partial Regex KeyPattern();

        public static bool IsKey(string? key) => key is { Length: > 0 and <= 64 } && KeyPattern().IsMatch(key);

        public SiteTemplatePalette? FindPalette(string? key) => Palettes.FirstOrDefault(p => p.Key == key);
        public SiteTemplateFont? FindFont(string? key) => Fonts.FirstOrDefault(f => f.Key == key);
        public SiteTemplateField? FindField(string? key) => Fields.FirstOrDefault(f => f.Key == key);
        public SiteTemplateCategory? FindCategory(string? slug) => Categories.FirstOrDefault(c => c.Slug == slug);

        /// <summary>
        /// Reads a template's schema and checks it holds together, so a mistake in a template is caught when
        /// the host starts and not when an owner saves.
        /// </summary>
        /// <exception cref="InvalidOperationException">With what is wrong, in plain words</exception>
        public static SiteTemplate FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("The template schema is empty.");
            }
            if (json.Length > MaxLength)
            {
                throw new InvalidOperationException($"The template schema is larger than {MaxLength / 1024} KB.");
            }

            SiteTemplate? template;
            try
            {
                template = JsonSerializer.Deserialize<SiteTemplate>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"The template schema is not valid JSON: {ex.Message}");
            }

            template = template ?? throw new InvalidOperationException("The template schema is empty.");
            template.Check();
            return template;
        }

        private void Check()
        {
            var name = $"Template {Key}@{Version}";
            Require(IsKey(Key), "A template needs a key of lowercase letters, digits, dots and dashes.");
            Require(Version >= 1, $"{name}: the version must be 1 or more.");
            Require(!string.IsNullOrWhiteSpace(Name), $"{name}: give it a name.");

            Require(Palettes.Count > 0, $"{name}: at least one palette is needed.");
            Unique(Palettes.Select(p => p.Key), $"{name}: palette");
            foreach (var palette in Palettes)
            {
                var label = $"{name}, palette {palette.Key}";
                Require(IsKey(palette.Key) && !string.IsNullOrWhiteSpace(palette.Name), $"{label}: needs a key and a name.");
                foreach (var (role, value) in new[]
                {
                    ("background", palette.Background), ("surface", palette.Surface), ("text", palette.Text),
                    ("muted", palette.Muted), ("accent", palette.Accent), ("onAccent", palette.OnAccent),
                })
                {
                    Require(SiteColors.IsHex(value), $"{label}: {role} must be a colour like #1f5f4f.");
                }

                // A palette the template offers must itself be readable, on both grounds text is set on
                foreach (var ground in new[] { palette.Background, palette.Surface })
                {
                    Require(SiteColors.Contrast(palette.Text, ground) >= SiteColors.TextContrast, $"{label}: text is too close to its background to read.");
                    Require(SiteColors.Contrast(palette.Muted, ground) >= SiteColors.TextContrast, $"{label}: muted text is too close to its background to read.");
                }
                Require(SiteContentRules.AccentProblem(palette, palette.Accent) == null, $"{label}: its own accent fails the rules a custom accent is held to.");
            }

            Require(Fonts.Count > 0, $"{name}: at least one font pairing is needed.");
            Unique(Fonts.Select(f => f.Key), $"{name}: font");
            Require(Fonts.All(f => IsKey(f.Key) && !string.IsNullOrWhiteSpace(f.Name)), $"{name}: every font pairing needs a key and a name.");

            Unique(Fields.Select(f => f.Key), $"{name}: field");
            foreach (var field in Fields)
            {
                var label = $"{name}, field {field.Key}";
                Require(IsKey(field.Key), $"{label}: the key must be lowercase letters, digits, dots and dashes.");
                Require(!string.IsNullOrWhiteSpace(field.Label) && !string.IsNullOrWhiteSpace(field.Group), $"{label}: needs a label and a group.");
                Require(SiteTemplateFieldTypes.All.Contains(field.Type), $"{label}: the type must be text or multiline.");
                Require(field.MaxLength is >= 1 and <= SiteContentRules.MaxFieldLength, $"{label}: the length limit must be between 1 and {SiteContentRules.MaxFieldLength}.");
                Require(SiteContentRules.TextProblem(field, field.Default ?? string.Empty, checkRequired: false) == null, $"{label}: its default breaks its own rules.");
                Require(!field.Required || !string.IsNullOrWhiteSpace(field.Default), $"{label}: a required field needs a default, so a new site is never missing it.");
            }

            Unique(Links, $"{name}: link");
            Require(Links.All(IsKey), $"{name}: link names must be lowercase letters, digits, dots and dashes.");

            Require(Categories.Count > 0, $"{name}: at least one category is needed.");
            Unique(Categories.Select(c => c.Slug), $"{name}: category");
            Require(Categories.All(c => IsKey(c.Slug) && !c.Slug.Contains('.') && SiteContentRules.CategoryNameProblem(c.Name) == null),
                $"{name}: every category needs a slug of lowercase letters, digits and dashes, and a name of {SiteContentRules.MaxCategoryNameLength} characters or fewer.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Unique(IEnumerable<string> keys, string what)
        {
            var repeated = keys.GroupBy(k => k, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            Require(repeated == null, $"{what} {repeated?.Key} is listed twice.");
        }
    }

    /// <summary>
    /// The template versions this host knows, registered when it starts. A tenant pinned to a version the host
    /// doesn't have can't be edited or published until the host is given it.
    /// </summary>
    public sealed class SiteTemplateCatalog(IEnumerable<SiteTemplate>? templates = null)
    {
        private readonly Dictionary<(string Key, int Version), SiteTemplate> templates =
            (templates ?? []).ToDictionary(t => (t.Key, t.Version));

        public IReadOnlyCollection<SiteTemplate> All => templates.Values;

        public SiteTemplate? Find(string? key, int? version) =>
            key != null && version != null && templates.TryGetValue((key, version.Value), out var template) ? template : null;

        /// <summary>
        /// Reads each schema; a host passes the template files it was built with
        /// </summary>
        public static SiteTemplateCatalog FromJson(params string[] schemas) => new(schemas.Select(SiteTemplate.FromJson));
    }
}
