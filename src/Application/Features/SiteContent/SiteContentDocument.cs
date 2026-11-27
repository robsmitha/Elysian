using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elysian.Application.Features.SiteContent
{
    /// <param name="Palette">Key of one of the template's palettes</param>
    /// <param name="Accent">The owner's own accent as #rrggbb, or null to use the palette's</param>
    /// <param name="Font">Key of one of the template's font pairings</param>
    public sealed record SiteContentTheme(string? Palette, string? Accent, string? Font);

    /// <param name="Name">The owner's name for the category, or null to use the template's</param>
    public sealed record SiteContentCategory(string Slug, string? Name, bool Enabled = true);

    public sealed record SiteContentProblem(string Field, string Message);

    /// <summary>
    /// The site's logo: which upload it is, and the size of the larger of its two files. The files are
    /// <c>placements/{tenant}/logo-{version}-{height}.webp</c> and the same at half the height (see
    /// <see cref="SiteLogoPaths"/>). A new upload is a new version, so a file never changes once written, and
    /// a draft can show a new logo while visitors still see the old one.
    /// </summary>
    /// <param name="Width">Of the larger file, in pixels</param>
    /// <param name="Height">Of the larger file, in pixels. Always even, so the smaller file is exactly half.</param>
    public sealed record SiteContentLogo(long Version, int Width, int Height);

    /// <summary>
    /// What a site's owner chose within their template: colours and fonts, wording, links, and which categories
    /// show. JSON shape: <c>{ theme: { palette, accent, font }, text: { "home.heading": "…" },
    /// links: { instagram: "https://…" }, categories: [{ slug, name, enabled }] }</c>.
    ///
    /// The same document is the draft an owner edits and, once published, what the edge serves. Everything in
    /// it ends up in pages served to visitors, so nothing is stored that wasn't checked against the template.
    /// </summary>
    public sealed class SiteContentDocument
    {
        /// <summary>
        /// Far more than a template's fields add up to; keeps a mistaken or hostile save from being stored
        /// </summary>
        public const int MaxLength = 64 * 1024;

        public SiteContentTheme Theme { get; init; } = new(null, null, null);

        /// <summary>
        /// Wording by field key. A field left out shows the template's default.
        /// </summary>
        public IReadOnlyDictionary<string, string> Text { get; init; } = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Links { get; init; } = new Dictionary<string, string>();

        /// <summary>
        /// The categories in the order the site lists them. One left out is as the template defines it: its
        /// name, and whether it is on to begin with.
        /// </summary>
        public IReadOnlyList<SiteContentCategory> Categories { get; init; } = [];

        /// <summary>
        /// The logo shown in place of the site's name, or null to show the name. Only uploading or removing a
        /// logo changes it (<see cref="UploadSiteLogoCommand"/>): saving the draft keeps what is stored.
        /// Left out of the JSON when there is none, so content from before logos reads and compares unchanged.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SiteContentLogo? Logo { get; init; }

        /// <summary>The same content with another logo, or none</summary>
        public SiteContentDocument WithLogo(SiteContentLogo? logo) => new()
        {
            Theme = Theme, Text = Text, Links = Links, Categories = Categories, Logo = logo,
        };

        /// <summary>
        /// A new site's content: the template's first palette and font, its default wording, and the
        /// categories the template has on to begin with
        /// </summary>
        public static SiteContentDocument DefaultFor(SiteTemplate template) => new()
        {
            Theme = new SiteContentTheme(template.Palettes[0].Key, null, template.Fonts[0].Key),
            Categories = [.. template.Categories.Select(c => new SiteContentCategory(c.Slug, null, c.Enabled))],
        };

        /// <exception cref="InvalidOperationException">When the text isn't a content document at all</exception>
        public static SiteContentDocument FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("The content is empty.");
            }
            if (json.Length > MaxLength)
            {
                throw new InvalidOperationException($"The content is larger than {MaxLength / 1024} KB.");
            }

            try
            {
                return JsonSerializer.Deserialize<SiteContentDocument>(json, SiteTemplate.JsonOptions)
                    ?? throw new InvalidOperationException("The content is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"The content is not valid JSON: {ex.Message}");
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this, SiteTemplate.JsonOptions);

        /// <summary>
        /// The same content in one spelling: values trimmed, colours lowercase, empty entries dropped, keys in
        /// a fixed order. Two documents that say the same thing normalize to the same JSON, which is how "has
        /// anything changed since it was published" is answered.
        /// </summary>
        public SiteContentDocument Normalize() => new()
        {
            Theme = new SiteContentTheme(
                Theme?.Palette?.Trim(),
                string.IsNullOrWhiteSpace(Theme?.Accent) ? null : Theme.Accent.Trim().ToLowerInvariant(),
                Theme?.Font?.Trim()),
            Text = Clean(Text, SiteContentRules.NormalizeText),
            Links = Clean(Links, value => value.Trim()),
            Categories = [.. (Categories ?? []).Select(c => new SiteContentCategory(
                (c.Slug ?? string.Empty).Trim(), string.IsNullOrWhiteSpace(c.Name) ? null : c.Name.Trim(), c.Enabled))],
            Logo = Logo,
        };

        private static SortedDictionary<string, string> Clean(IReadOnlyDictionary<string, string>? values, Func<string, string> clean)
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in values ?? new Dictionary<string, string>())
            {
                var cleaned = clean(value ?? string.Empty);
                if (cleaned.Length > 0)
                {
                    result[key] = cleaned;
                }
            }
            return result;
        }

        /// <summary>
        /// Everything wrong with this content for a template, each against the field it concerns
        /// (e.g. "text.home.heading"). Empty when it can be stored. Expects a normalized document.
        /// </summary>
        public List<SiteContentProblem> Validate(SiteTemplate template)
        {
            var problems = new List<SiteContentProblem>();
            void Add(string field, string? message)
            {
                if (message != null)
                {
                    problems.Add(new SiteContentProblem(field, message));
                }
            }

            var palette = template.FindPalette(Theme.Palette);
            Add("theme.palette", palette == null ? "Choose one of the colour palettes." : null);
            Add("theme.font", template.FindFont(Theme.Font) == null ? "Choose one of the font pairings." : null);
            if (Theme.Accent != null)
            {
                Add("theme.accent",
                    !template.AllowCustomAccent ? "This template doesn't take a custom accent colour."
                    : !SiteColors.IsHex(Theme.Accent) ? "Enter the colour as # and six letters or digits, like #1f5f4f."
                    : palette != null ? SiteContentRules.AccentProblem(palette, Theme.Accent)
                    : null);
            }

            foreach (var (key, value) in Text)
            {
                var field = template.FindField(key);
                Add($"text.{key}", field == null ? "This isn't one of the template's fields." : SiteContentRules.TextProblem(field, value, checkRequired: false));
            }
            // A required field left out falls back to its default, so only one that was emptied on purpose
            // could be missing, and normalizing has already dropped that. Nothing more to check here.

            foreach (var (key, value) in Links)
            {
                Add($"links.{key}", !template.Links.Contains(key) ? "This isn't one of the template's links." : SiteContentRules.LinkProblem(value));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var category in Categories)
            {
                var field = $"categories.{category.Slug}";
                if (template.FindCategory(category.Slug) == null)
                {
                    Add(field, "This isn't one of the template's categories.");
                }
                else if (!seen.Add(category.Slug))
                {
                    Add(field, "This category is listed twice.");
                }
                else if (category.Name != null)
                {
                    Add(field, SiteContentRules.CategoryNameProblem(category.Name));
                }
            }

            if (Logo != null)
            {
                Add("logo", SiteLogoPaths.IsWellFormed(Logo) ? null : "The logo isn't one that was uploaded here. Upload it again.");
            }

            // What the owner said, and for a category they never mentioned, what the template says
            var chosen = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var category in Categories)
            {
                chosen.TryAdd(category.Slug, category.Enabled);
            }
            if (template.Categories.All(c => !(chosen.TryGetValue(c.Slug, out var enabled) ? enabled : c.Enabled)))
            {
                Add("categories", "Keep at least one category switched on.");
            }

            return problems;
        }
    }

    /// <summary>
    /// The rules a single value is held to. Each returns what is wrong in words for the owner, or null.
    /// </summary>
    public static class SiteContentRules
    {
        public const int MaxFieldLength = 5000;
        public const int MaxLinkLength = 300;
        public const int MaxCategoryNameLength = 40;

        /// <summary>
        /// Trimmed, with Windows line endings made plain, so a length means the same wherever it was typed
        /// </summary>
        public static string NormalizeText(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        public static string? TextProblem(SiteTemplateField field, string value, bool checkRequired = true)
        {
            if (checkRequired && field.Required && string.IsNullOrWhiteSpace(value))
            {
                return $"{field.Label} can't be empty.";
            }
            if (value.Length > field.MaxLength)
            {
                return $"{field.Label} must be {field.MaxLength} characters or fewer.";
            }
            if (field.Type != SiteTemplateFieldTypes.Multiline && value.Contains('\n'))
            {
                return $"{field.Label} must be a single line.";
            }
            // Tabs and line breaks aside, a control character is never something a person typed on purpose
            if (value.Any(c => char.IsControl(c) && c != '\n' && c != '\t'))
            {
                return $"{field.Label} has characters that can't be shown.";
            }
            return null;
        }

        /// <summary>
        /// A link is an ordinary https address and nothing else: no other scheme (javascript:, data:, mailto:),
        /// no sign-in details in it, and a real host name.
        /// </summary>
        public static string? LinkProblem(string value)
        {
            const string message = "Enter a full web address starting with https://";
            if (value.Length > MaxLinkLength)
            {
                return $"A link must be {MaxLinkLength} characters or fewer.";
            }
            if (value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            {
                return message;
            }
            if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.HostNameType != UriHostNameType.Dns
                || !uri.Host.Contains('.'))
            {
                return message;
            }
            return null;
        }

        public static string? CategoryNameProblem(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "A category needs a name.";
            }
            if (name.Length > MaxCategoryNameLength)
            {
                return $"A category name must be {MaxCategoryNameLength} characters or fewer.";
            }
            return name.Any(char.IsControl) ? "A category name must be a single line." : null;
        }

        /// <summary>
        /// An accent is used two ways: as a button with text on it, and as coloured text and lines on the
        /// page. It has to read in both.
        /// </summary>
        public static string? AccentProblem(SiteTemplatePalette palette, string accent)
        {
            if (SiteColors.Contrast(accent, palette.OnAccent) < SiteColors.TextContrast)
            {
                return "Button text wouldn't be readable on this colour. Try a darker or lighter shade.";
            }
            if (SiteColors.Contrast(accent, palette.Background) < SiteColors.LargeContrast)
            {
                return "This colour is too close to the page's background to stand out. Try a darker or lighter shade.";
            }
            return null;
        }
    }
}
