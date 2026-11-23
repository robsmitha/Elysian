using Elysian.Application.Exceptions;
using Elysian.Application.Features.SiteContent;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Elysian.Tests.Sites
{
    public sealed class SiteContentTests : IDisposable
    {
        private const string Schema = """
            {
              "key": "classic", "version": 1, "name": "Classic",
              "allowCustomAccent": true,
              "palettes": [
                { "key": "paper", "name": "Paper", "background": "#ffffff", "surface": "#f3f5f7", "text": "#16181d", "muted": "#4a5160", "accent": "#1f5f4f", "onAccent": "#ffffff" },
                { "key": "night", "name": "Night", "background": "#101214", "surface": "#181b1f", "text": "#f2f3f5", "muted": "#b3b9c2", "accent": "#e8b84a", "onAccent": "#101214" }
              ],
              "fonts": [ { "key": "sans", "name": "Modern sans" }, { "key": "serif", "name": "Classic serif" } ],
              "fields": [
                { "key": "home.heading", "label": "Home heading", "group": "Home", "maxLength": 60, "required": true, "default": "Photographs you will want on the wall." },
                { "key": "home.intro", "label": "Home introduction", "group": "Home", "type": "multiline", "maxLength": 200 },
                { "key": "footer.line", "label": "Footer line", "group": "Footer", "maxLength": 40 }
              ],
              "links": [ "instagram", "facebook" ],
              "categories": [ { "slug": "weddings", "name": "Weddings" }, { "slug": "families", "name": "Families" } ]
            }
            """;

        private static readonly SiteTemplate Template = SiteTemplate.FromJson(Schema);

        private readonly TestHost host = new();
        private readonly PlatformContext platform;
        private readonly SiteTemplateCatalog catalog = new([Template]);
        private readonly RecordingEdge edge = new();
        private ElysianTenantInfo tenant = TestHost.Tenant with { TemplateKey = "classic", TemplateVersion = 1 };

        public SiteContentTests()
        {
            platform = new PlatformContext(new DbContextOptionsBuilder<PlatformContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            platform.Tenants.Add(tenant with { });
            platform.SaveChanges();
        }

        public void Dispose()
        {
            platform.Dispose();
            host.Dispose();
        }

        // ---- The template's own schema

        [Fact]
        public void A_schema_that_holds_together_is_read()
        {
            Assert.Equal(("classic", 1), (Template.Key, Template.Version));
            Assert.Equal(2, Template.Palettes.Count);
            Assert.Equal(SiteTemplateFieldTypes.Multiline, Template.FindField("home.intro")!.Type);
            Assert.Same(Template, catalog.Find("classic", 1));
            Assert.Null(catalog.Find("classic", 2));
            Assert.Null(catalog.Find(null, null));
        }

        [Theory]
        [InlineData("\"background\": \"#ffffff\"", "\"background\": \"white\"", "must be a colour")]
        [InlineData("\"muted\": \"#4a5160\"", "\"muted\": \"#c8ccd2\"", "muted text is too close")]
        [InlineData("\"accent\": \"#1f5f4f\"", "\"accent\": \"#f4f4f4\"", "its own accent fails")]
        [InlineData("\"key\": \"night\"", "\"key\": \"paper\"", "listed twice")]
        [InlineData("\"key\": \"footer.line\"", "\"key\": \"Footer Line\"", "the key must be")]
        [InlineData("\"type\": \"multiline\"", "\"type\": \"html\"", "text or multiline")]
        [InlineData("\"required\": true, \"default\": \"Photographs you will want on the wall.\"", "\"required\": true", "needs a default")]
        [InlineData("\"maxLength\": 60, \"required\": true", "\"maxLength\": 10, \"required\": true", "default breaks its own rules")]
        [InlineData("\"version\": 1", "\"version\": 0", "version must be 1 or more")]
        [InlineData("\"slug\": \"families\"", "\"slug\": \"Our Families\"", "every category needs a slug")]
        public void A_schema_with_a_mistake_is_refused_with_the_reason(string find, string replace, string expected)
        {
            Assert.Contains(find, Schema);
            var error = Assert.Throws<InvalidOperationException>(() => SiteTemplate.FromJson(Schema.Replace(find, replace)));
            Assert.Contains(expected, error.Message);
        }

        // The schema above with its wording laid out on pages
        private static string WithPages(string pages) => Schema
            .Replace("\"group\": \"Home\"", "\"group\": \"Home\", \"section\": \"home.top\", \"help\": \"The first thing visitors read.\"")
            .Replace("\"group\": \"Footer\"", "\"group\": \"Footer\", \"section\": \"site.footer\"")
            .Replace("\"links\":", $"\"pages\": {pages}, \"links\":");

        private const string Pages = """
            [
              { "key": "home", "name": "Home", "path": "/", "sections": [ { "key": "home.top", "name": "Top of the page" } ] },
              { "key": "booking", "name": "Booking", "path": "/booking", "module": "booking", "sections": [ { "key": "booking.top", "name": "Top of the page" } ] },
              { "key": "site", "name": "Name and footer", "path": "/", "wholeSite": true, "sections": [
                { "key": "site.footer", "name": "Footer" }, { "key": "site.links", "name": "Social links", "kind": "links" } ] }
            ]
            """;

        [Fact]
        public void A_schema_can_lay_its_wording_out_on_pages()
        {
            var template = SiteTemplate.FromJson(WithPages(Pages));

            Assert.Equal(["home", "booking", "site"], template.Pages.Select(p => p.Key));
            Assert.Equal(("booking", false), (template.Pages[1].Module, template.Pages[1].WholeSite));
            Assert.True(template.Pages[2].WholeSite);
            Assert.Equal([SiteTemplateSection.Fields, "links"], template.Pages[2].Sections.Select(s => s.Kind));
            Assert.Equal(("home.top", "The first thing visitors read."), (template.FindField("home.heading")!.Section, template.FindField("home.heading")!.Help));

            // A template that doesn't say is still a template
            Assert.Empty(Template.Pages);
            Assert.Null(Template.FindField("home.heading")!.Section);
        }

        [Theory]
        [InlineData("\"key\": \"home.top\"", "\"key\": \"site.footer\"", "section site.footer is listed twice")]
        [InlineData("\"key\": \"booking\", \"name\"", "\"key\": \"home\", \"name\"", "page home is listed twice")]
        [InlineData("\"path\": \"/booking\"", "\"path\": \"booking\"", "the path must start with one /")]
        [InlineData("\"path\": \"/booking\"", "\"path\": \"//elsewhere.example\"", "the path must start with one /")]
        [InlineData("[ { \"key\": \"booking.top\", \"name\": \"Top of the page\" } ]", "[]", "at least one section is needed")]
        [InlineData("\"key\": \"site.footer\", \"name\"", "\"key\": \"site.foot\", \"name\"", "there is no section site.footer")]
        [InlineData("\"kind\": \"links\"", "\"kind\": \"Not A Kind\"", "every section needs a key, a name and a kind")]
        public void Pages_with_a_mistake_are_refused_with_the_reason(string find, string replace, string expected)
        {
            Assert.Contains(find, Pages);
            var error = Assert.Throws<InvalidOperationException>(() => SiteTemplate.FromJson(WithPages(Pages.Replace(find, replace))));
            Assert.Contains(expected, error.Message);
        }

        [Fact]
        public void Once_pages_are_listed_every_field_has_a_place_on_one()
        {
            var json = WithPages(Pages).Replace(", \"section\": \"site.footer\"", "");
            var error = Assert.Throws<InvalidOperationException>(() => SiteTemplate.FromJson(json));
            Assert.Contains("every field needs a section", error.Message);
        }

        [Fact]
        public void A_schema_that_is_not_json_is_refused()
        {
            Assert.Throws<InvalidOperationException>(() => SiteTemplate.FromJson("{ not json"));
            Assert.Throws<InvalidOperationException>(() => SiteTemplate.FromJson(" "));
        }

        // ---- Colours

        [Fact]
        public void Contrast_is_the_wcag_ratio()
        {
            Assert.Equal(21, SiteColors.Contrast("#000000", "#FFFFFF"), 2);
            Assert.Equal(1, SiteColors.Contrast("#1f5f4f", "#1F5F4F"), 2);
            // A grey that is known to sit just under AA on white
            Assert.InRange(SiteColors.Contrast("#777777", "#ffffff"), 4.4, 4.5);
        }

        [Theory]
        [InlineData("#1f5f4f", true)]
        [InlineData(" #1F5F4F ", true)]
        [InlineData("1f5f4f", false)]
        [InlineData("#fff", false)]
        [InlineData("#1f5f4g", false)]
        [InlineData("red", false)]
        [InlineData("#1f5f4f;background:url(x)", false)]
        [InlineData("var(--x)", false)]
        [InlineData(null, false)]
        public void Only_six_digit_hex_is_a_colour(string? value, bool expected) => Assert.Equal(expected, SiteColors.IsHex(value));

        // ---- Checking content

        [Fact]
        public void A_new_sites_defaults_are_valid()
        {
            var content = SiteContentDocument.DefaultFor(Template).Normalize();
            Assert.Empty(content.Validate(Template));
            Assert.Equal("paper", content.Theme.Palette);
            Assert.Equal(["weddings", "families"], content.Categories.Select(c => c.Slug));
        }

        [Fact]
        public void Content_is_stored_in_one_spelling()
        {
            var content = Content(theme: new("paper", " #2A4F8F ", "sans"),
                text: new() { ["home.intro"] = "  Line one\r\nLine two  ", ["footer.line"] = "   " },
                links: new() { ["instagram"] = " https://www.instagram.com/harborandpine " }).Normalize();

            Assert.Equal("#2a4f8f", content.Theme.Accent);
            Assert.Equal("Line one\nLine two", content.Text["home.intro"]);
            Assert.False(content.Text.ContainsKey("footer.line"));
            Assert.Equal("https://www.instagram.com/harborandpine", content.Links["instagram"]);
            Assert.Empty(content.Validate(Template));

            // Saying the same thing twice gives the same document
            Assert.Equal(content.ToJson(), SiteContentDocument.FromJson(content.ToJson()).Normalize().ToJson());
        }

        [Theory]
        [InlineData("theme.palette", "neon", null, "sans")]
        [InlineData("theme.font", "paper", null, "comic")]
        [InlineData("theme.accent", "paper", "#f2f2f2", "sans")]
        [InlineData("theme.accent", "paper", "#ff0", "sans")]
        [InlineData("theme.accent", "night", "#1f5f4f", "sans")]
        public void A_theme_outside_the_template_is_refused(string field, string palette, string? accent, string font)
        {
            var problems = Content(theme: new(palette, accent, font)).Normalize().Validate(Template);
            Assert.Contains(problems, p => p.Field == field);
        }

        [Fact]
        public void An_accent_that_reads_on_one_palette_may_not_on_another()
        {
            // Dark green: white button text reads on it and it stands out on white, but it vanishes on near-black
            Assert.Null(SiteContentRules.AccentProblem(Template.FindPalette("paper")!, "#1f5f4f"));
            Assert.NotNull(SiteContentRules.AccentProblem(Template.FindPalette("night")!, "#1f5f4f"));
            Assert.Null(SiteContentRules.AccentProblem(Template.FindPalette("night")!, "#8fd0b4"));
        }

        [Fact]
        public void Wording_is_held_to_its_field()
        {
            var problems = Content(text: new()
            {
                ["home.heading"] = new string('a', 61),
                ["footer.line"] = "two\nlines",
                ["home.intro"] = "fine\nover two lines",
                ["home.secret"] = "not a field",
            }).Normalize().Validate(Template);

            Assert.Equal(["text.footer.line", "text.home.heading", "text.home.secret"], problems.Select(p => p.Field).Order());
            Assert.Contains("60 characters or fewer", problems.Single(p => p.Field == "text.home.heading").Message);
        }

        [Fact]
        public void Markup_in_wording_is_kept_as_the_text_it_is()
        {
            // Nothing is stripped here: whatever draws the page escapes it. It only has to fit the field.
            var content = Content(text: new() { ["footer.line"] = "<b>Made</b> with \"care\" & tea" }).Normalize();
            Assert.Empty(content.Validate(Template));
            Assert.Equal("<b>Made</b> with \"care\" & tea", content.Text["footer.line"]);
        }

        [Theory]
        [InlineData("https://www.instagram.com/harborandpine", true)]
        [InlineData("HTTPS://Example.com/a?b=c", true)]
        [InlineData("http://www.instagram.com/x", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("data:text/html,<script>", false)]
        [InlineData("//example.com/x", false)]
        [InlineData("https://user:pass@example.com", false)]
        [InlineData("https://localhost/x", false)]
        [InlineData("https://192.168.0.1/x", false)]
        [InlineData("https://example.com/a b", false)]
        [InlineData("https://example.com/\nx", false)]
        [InlineData("mailto:a@example.com", false)]
        public void A_link_is_an_ordinary_https_address(string link, bool ok) =>
            Assert.Equal(ok, SiteContentRules.LinkProblem(link) == null);

        [Fact]
        public void Links_and_categories_must_be_the_templates()
        {
            var problems = Content(
                links: new() { ["tiktok"] = "https://www.tiktok.com/@x", ["facebook"] = "http://facebook.com/x" },
                categories: [new("weddings", "Wedding days"), new("weddings", null), new("pets", null), new("families", new string('x', 41))])
                .Normalize().Validate(Template);

            Assert.Equal(["categories.families", "categories.pets", "categories.weddings", "links.facebook", "links.tiktok"], problems.Select(p => p.Field).Order());
        }

        [Fact]
        public void At_least_one_category_stays_on()
        {
            var problems = Content(categories: [new("weddings", null, false), new("families", null, false)]).Normalize().Validate(Template);
            Assert.Equal("categories", Assert.Single(problems).Field);

            Assert.Empty(Content(categories: [new("weddings", null, false)]).Normalize().Validate(Template));
        }

        // ---- Draft and publish

        [Fact]
        public async Task A_site_nobody_has_edited_shows_the_templates_defaults()
        {
            var model = await GetAsync();

            Assert.Same(Template, model.Template);
            Assert.Equal("paper", model.Draft.Theme.Palette);
            Assert.Null(model.PublishedVersion);
            Assert.True(model.HasUnpublishedChanges);
        }

        [Fact]
        public async Task Saving_stores_the_draft_and_changes_nothing_for_visitors()
        {
            var saved = await SaveAsync(Content(text: new() { ["home.heading"] = "  Hello  " }));
            Assert.Equal("Hello", saved.Text["home.heading"]);

            var model = await GetAsync();
            Assert.Equal("Hello", model.Draft.Text["home.heading"]);
            Assert.Null(model.PublishedVersion);
            Assert.Empty(edge.Calls);

            var row = await host.Context.SiteContentDrafts.SingleAsync();
            Assert.Equal(("classic", 1), (row.TemplateKey, row.TemplateVersion));
        }

        [Fact]
        public async Task Saving_content_with_a_problem_stores_nothing()
        {
            await SaveAsync(Content(text: new() { ["home.heading"] = "Kept" }));

            var error = await Assert.ThrowsAsync<CustomValidationException>(() => SaveAsync(Content(
                theme: new("paper", "#ffffff", "sans"), text: new() { ["home.heading"] = new string('a', 100) })));
            Assert.Equal(["text.home.heading", "theme.accent"], error.Errors.Keys.Order());

            Assert.Equal("Kept", (await GetAsync()).Draft.Text["home.heading"]);
            await Assert.ThrowsAsync<CustomValidationException>(() => SaveAsync(null));
        }

        [Fact]
        public async Task Publishing_writes_the_version_to_the_edge_before_pointing_at_it()
        {
            await SaveAsync(Content(text: new() { ["home.heading"] = "Hello" }));

            var first = await PublishAsync();
            Assert.Equal(1, first.Version);
            Assert.Equal(["write test 1 classic@1", "point test 1"], edge.Calls);
            Assert.Contains("\"home.heading\":\"Hello\"", edge.Written[1]);
            Assert.Equal(1, (await platform.Tenants.AsNoTracking().SingleAsync()).ContentVersion);

            var model = await GetAsync();
            Assert.Equal(1, model.PublishedVersion);
            Assert.False(model.HasUnpublishedChanges);

            await SaveAsync(Content(text: new() { ["home.heading"] = "Hello again" }));
            Assert.True((await GetAsync()).HasUnpublishedChanges);

            Assert.Equal(2, (await PublishAsync()).Version);
            Assert.Equal(2, await host.Context.SiteContentVersions.CountAsync());
            Assert.Equal(2, (await platform.Tenants.AsNoTracking().SingleAsync()).ContentVersion);
        }

        [Fact]
        public async Task A_site_can_be_published_with_the_defaults()
        {
            Assert.Equal(1, (await PublishAsync()).Version);
            Assert.Contains("\"palette\":\"paper\"", edge.Written[1]);
        }

        [Fact]
        public async Task When_the_edge_cannot_be_written_the_live_version_does_not_move()
        {
            await PublishAsync();
            edge.FailWrites = true;

            await Assert.ThrowsAsync<InvalidOperationException>(PublishAsync);
            Assert.DoesNotContain("point test 2", edge.Calls);
            Assert.Equal(1, (await platform.Tenants.AsNoTracking().SingleAsync()).ContentVersion);

            // Trying again makes a fresh version and goes through
            edge.FailWrites = false;
            Assert.Equal(3, (await PublishAsync()).Version);
            Assert.Equal(3, (await platform.Tenants.AsNoTracking().SingleAsync()).ContentVersion);
        }

        [Fact]
        public async Task A_host_with_no_edge_store_says_so_when_publishing()
        {
            var handler = new PublishSiteContentCommandHandler(host.Context, platform, Reader(), new UnconfiguredSiteContentEdge(),
                NullLogger<PublishSiteContentCommandHandler>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new PublishSiteContentCommand(), CancellationToken.None));
            Assert.Equal(UnconfiguredSiteContentEdge.Message, error.Message);
        }

        [Fact]
        public async Task A_draft_that_no_longer_fits_the_template_cannot_be_published()
        {
            host.Context.SiteContentDrafts.Add(new SiteContentDraft
            {
                Json = Content(text: new() { ["home.retired"] = "From an older template" }).Normalize().ToJson(),
                TemplateKey = "classic",
                TemplateVersion = 1,
            });
            await host.Context.SaveChangesAsync();

            var error = await Assert.ThrowsAsync<CustomValidationException>(PublishAsync);
            Assert.Equal(["text.home.retired"], error.Errors.Keys);
            Assert.Empty(edge.Calls);
        }

        [Fact]
        public async Task A_site_with_its_own_code_has_no_content_to_edit()
        {
            tenant = TestHost.Tenant with { TemplateKey = null, TemplateVersion = null };
            await Assert.ThrowsAsync<ConflictException>(GetAsync);
            await Assert.ThrowsAsync<ConflictException>(() => SaveAsync(Content()));
            await Assert.ThrowsAsync<ConflictException>(PublishAsync);

            tenant = TestHost.Tenant with { TemplateKey = "classic", TemplateVersion = 9 };
            var error = await Assert.ThrowsAsync<ConflictException>(GetAsync);
            Assert.Contains("version 9", error.Message);
        }

        // ---- Preview

        [Fact]
        public async Task A_preview_link_opens_the_draft_until_it_expires()
        {
            var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-22T12:00:00Z"));
            var tokens = Tokens(time);
            await SaveAsync(Content(text: new() { ["home.heading"] = "Unpublished" }));

            var link = await new CreateSitePreviewLinkCommandHandler(Reader(), tokens).Handle(new CreateSitePreviewLinkCommand(), CancellationToken.None);
            Assert.Equal(time.GetUtcNow().AddMinutes(60), link.ExpiresAt);

            var preview = await PreviewAsync(tokens, link.Token);
            Assert.Equal(("classic", 1), (preview.TemplateKey, preview.TemplateVersion));
            Assert.Equal("Unpublished", preview.Content.Text["home.heading"]);

            time.Advance(TimeSpan.FromMinutes(61));
            await Assert.ThrowsAsync<NotFoundException>(() => PreviewAsync(tokens, link.Token));
        }

        [Fact]
        public async Task A_preview_link_is_good_for_one_site_and_one_purpose()
        {
            var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-22T12:00:00Z"));
            var tokens = Tokens(time);
            var (token, _) = tokens.Issue("test");

            Assert.True(tokens.IsValid("test", token));
            Assert.False(tokens.IsValid("other", token));
            Assert.False(tokens.IsValid("test", token[..^2] + "AA"));
            Assert.False(tokens.IsValid("test", "not-a-token"));
            Assert.False(tokens.IsValid("test", null));

            // A story preview token, signed with the same key, is not a site preview token
            var storyToken = new HmacStoryPreviewTokens(Settings(), time).Issue("test", Guid.NewGuid()).Token;
            Assert.False(tokens.IsValid("test", storyToken));

            await Assert.ThrowsAsync<NotFoundException>(() => PreviewAsync(tokens, null));
        }

        // ---- Who may do what

        [Fact]
        public void An_editor_prepares_content_and_an_owner_publishes_it()
        {
            var editor = PolicyNames.GetDefaultPolicies([RoleNames.Editor]);
            Assert.Contains(PolicyNames.SiteRead, editor);
            Assert.Contains(PolicyNames.SiteWrite, editor);
            Assert.DoesNotContain(PolicyNames.SitePublish, editor);

            var owner = PolicyNames.GetDefaultPolicies([RoleNames.Owner]);
            Assert.Contains(PolicyNames.SiteWrite, owner);
            Assert.Contains(PolicyNames.SitePublish, owner);

            Assert.DoesNotContain(PolicyNames.SiteRead, PolicyNames.GetDefaultPolicies([RoleNames.Authenticated]));
        }

        // ---- Helpers

        private static SiteContentDocument Content(SiteContentTheme? theme = null, Dictionary<string, string>? text = null,
            Dictionary<string, string>? links = null, List<SiteContentCategory>? categories = null) => new()
        {
            Theme = theme ?? new SiteContentTheme("paper", null, "sans"),
            Text = text ?? [],
            Links = links ?? [],
            Categories = categories ?? [],
        };

        private SiteContentReader Reader()
        {
            var accessor = new Mock<IMultiTenantContextAccessor<ElysianTenantInfo>>();
            accessor.Setup(a => a.MultiTenantContext).Returns(() => new MultiTenantContext<ElysianTenantInfo> { TenantInfo = tenant });
            return new SiteContentReader(host.Context, catalog, accessor.Object);
        }

        private Task<SiteContentModel> GetAsync() =>
            new GetSiteContentQueryHandler(host.Context, Reader()).Handle(new GetSiteContentQuery(), CancellationToken.None);

        private Task<SiteContentDocument> SaveAsync(SiteContentDocument? content) =>
            new SaveSiteContentDraftCommandHandler(host.Context, Reader()).Handle(new SaveSiteContentDraftCommand(content), CancellationToken.None);

        private Task<SiteContentPublishedModel> PublishAsync() =>
            new PublishSiteContentCommandHandler(host.Context, platform, Reader(), edge, NullLogger<PublishSiteContentCommandHandler>.Instance)
                .Handle(new PublishSiteContentCommand(), CancellationToken.None);

        private Task<SiteContentPreviewModel> PreviewAsync(ISitePreviewTokens tokens, string? token) =>
            new GetSiteContentPreviewQueryHandler(Reader(), tokens).Handle(new GetSiteContentPreviewQuery(token), CancellationToken.None);

        private static IOptions<StoryPreviewSettings> Settings() =>
            Options.Create(new StoryPreviewSettings { SigningKey = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray()) });

        private static HmacSitePreviewTokens Tokens(TimeProvider time) => new(Settings(), time);

        private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
        {
            private DateTimeOffset now = now;
            public override DateTimeOffset GetUtcNow() => now;
            public void Advance(TimeSpan by) => now += by;
        }

        private sealed class RecordingEdge : ISiteContentEdge
        {
            public List<string> Calls { get; } = [];
            public Dictionary<int, string> Written { get; } = [];
            public bool FailWrites { get; set; }

            public Task WriteVersionAsync(string tenantIdentifier, int version, string templateKey, int templateVersion, string json, CancellationToken cancellationToken)
            {
                if (FailWrites)
                {
                    throw new InvalidOperationException("The edge store could not be reached.");
                }
                Calls.Add($"write {tenantIdentifier} {version} {templateKey}@{templateVersion}");
                Written[version] = json;
                return Task.CompletedTask;
            }

            public Task PointToVersionAsync(string tenantIdentifier, int version, CancellationToken cancellationToken)
            {
                Calls.Add($"point {tenantIdentifier} {version}");
                return Task.CompletedTask;
            }
        }
    }
}
