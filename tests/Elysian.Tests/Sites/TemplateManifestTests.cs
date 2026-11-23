using System.Text.Json;
using Elysian.Application.Features.Placements;
using Elysian.Application.Features.SiteContent;
using Elysian.Application.Features.Sites;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;

namespace Elysian.Tests.Sites
{
    /// <summary>
    /// A site drawn with a shared template takes its manifest from the template
    /// </summary>
    public sealed class TemplateManifestTests : IDisposable
    {
        private const string Schema = """
            {
              "key": "classic", "version": 1, "name": "Classic",
              "palettes": [ { "key": "paper", "name": "Paper", "background": "#ffffff", "surface": "#f3f5f7", "text": "#16181d", "muted": "#4a5160", "accent": "#1f5f4f", "onAccent": "#ffffff" } ],
              "fonts": [ { "key": "sans", "name": "Sans" } ],
              "fields": [],
              "links": [],
              "categories": [ { "slug": "weddings", "name": "Weddings" }, { "slug": "families", "name": "Families" } ]
            }
            """;

        private const string Manifest = """
            {
              "version": 1,
              "site": { "name": "", "publicUrl": "", "categoryPath": "/portfolio/{category}" },
              "categories": [
                { "slug": "weddings", "name": "Weddings", "hasGallery": true, "hasStories": true },
                { "slug": "families", "name": "Families", "hasGallery": true, "hasStories": false }
              ],
              "pages": [
                { "key": "home", "name": "Home", "path": "/", "layout": [ { "type": "spot", "spot": "home-hero" } ] },
                { "key": "site", "name": "Whole site", "path": "/" }
              ],
              "spots": [
                { "key": "home-hero", "label": "Hero", "page": "home" },
                { "key": "portfolio-cover-weddings", "label": "Weddings cover", "page": "home" },
                { "key": "site-default", "label": "Fallback", "page": "site" }
              ],
              "placements": {
                "recipeVersion": 1,
                "siteDefaultKey": "site-default",
                "slots": [
                  { "key": "home-hero", "name": "home-hero", "ratio": 2.2, "widths": [640, 1280], "alt": "Featured" },
                  { "key": "portfolio-cover-weddings", "name": "portfolio-cover-weddings", "ratio": 0.6667, "widths": [320, 480], "alt": "Weddings", "fallbackCategory": "weddings" }
                ]
              }
            }
            """;

        private readonly TestHost host = new();
        private readonly MemoryCache cache = new(new MemoryCacheOptions());
        private readonly SiteTemplateCatalog catalog = SiteTemplateCatalog.FromFiles([(Schema, Manifest)]);
        private ElysianTenantInfo tenant = TestHost.Tenant with { Name = "Harbor & Pine", Runtime = RuntimeNames.Cloud, TemplateKey = "classic", TemplateVersion = 1 };

        public void Dispose()
        {
            cache.Dispose();
            host.Dispose();
        }

        [Fact]
        public async Task A_site_on_a_template_gets_the_templates_manifest_made_out_for_it()
        {
            var manifest = await Reader().GetAsync();

            Assert.NotNull(manifest);
            Assert.Equal(SiteManifestReader.TemplateSource, manifest.Source);
            Assert.Equal("Harbor & Pine", manifest.Document.Site!.Name);
            Assert.Equal(2, manifest.Document.Placements!.Slots.Count);
            Assert.Equal(["weddings", "families"], manifest.Document.Categories.Select(c => c.Slug));

            // What the admin app reads beyond Elysian's own fields is carried through untouched
            var json = JsonDocument.Parse(manifest.Json).RootElement;
            Assert.Equal("/portfolio/{category}", json.GetProperty("site").GetProperty("categoryPath").GetString());
            Assert.Equal("home-hero", json.GetProperty("pages")[0].GetProperty("layout")[0].GetProperty("spot").GetString());
        }

        [Fact]
        public async Task Its_address_is_its_primary_one_or_a_placeholder_until_it_has_one()
        {
            Assert.Equal("https://test.invalid", (await Reader().GetAsync())!.Document.Site!.PublicUrl);

            host.Context.TenantDomains.Add(new TenantDomain { Host = "other.framenice.com", Kind = TenantDomainKinds.Subdomain, Status = TenantDomainStatuses.Active, IsPrimary = false });
            host.Context.TenantDomains.Add(new TenantDomain { Host = "www.harborandpine.com", Kind = TenantDomainKinds.Custom, Status = TenantDomainStatuses.Active, IsPrimary = true });
            await host.Context.SaveChangesAsync();
            cache.Clear();

            Assert.Equal("https://www.harborandpine.com", (await Reader().GetAsync())!.Document.Site!.PublicUrl);
        }

        [Fact]
        public async Task It_carries_the_owners_names_for_categories_from_the_draft()
        {
            var content = new SiteContentDocument
            {
                Theme = new SiteContentTheme("paper", null, "sans"),
                Categories = [new SiteContentCategory("weddings", "Wedding days"), new SiteContentCategory("families", null, false)],
            }.Normalize();
            host.Context.SiteContentDrafts.Add(new SiteContentDraft { Json = content.ToJson(), TemplateKey = "classic", TemplateVersion = 1 });
            await host.Context.SaveChangesAsync();

            var manifest = await Reader().GetAsync();

            // Renamed; and one switched off is still there, because its photos still need managing
            Assert.Equal(["Wedding days", "Families"], manifest!.Document.Categories.Select(c => c.Name));
        }

        [Fact]
        public async Task Saving_the_draft_shows_in_the_manifest_at_once()
        {
            var reader = Reader();
            Assert.Equal("Weddings", (await reader.GetAsync())!.Document.Categories[0].Name);

            var accessor = Accessor();
            var save = new SaveSiteContentDraftCommandHandler(host.Context, new SiteContentReader(host.Context, catalog, accessor), reader);
            await save.Handle(new SaveSiteContentDraftCommand(new SiteContentDocument
            {
                Theme = new SiteContentTheme("paper", null, "sans"),
                Categories = [new SiteContentCategory("weddings", "Wedding days")],
            }), CancellationToken.None);

            Assert.Equal("Wedding days", (await reader.GetAsync())!.Document.Categories[0].Name);
        }

        [Fact]
        public async Task The_photo_publisher_uses_the_templates_slots()
        {
            var source = new TenantPlacementCatalogSource(Reader(), new HostPlacementCatalog(null));
            var slots = await source.GetAsync();

            Assert.Equal(["home-hero", "portfolio-cover-weddings"], slots!.Slots.Select(s => s.Key));
            Assert.Equal("site-default", slots.SiteDefaultKey);
        }

        [Fact]
        public async Task A_manifest_the_site_stored_itself_wins()
        {
            host.Context.SiteManifests.Add(new SiteManifest
            {
                Json = """{ "version": 1, "site": { "name": "Own", "publicUrl": "https://own.example.com" } }""",
                Source = "deploy",
            });
            await host.Context.SaveChangesAsync();

            var manifest = await Reader().GetAsync();
            Assert.Equal("deploy", manifest!.Source);
            Assert.Equal("Own", manifest.Document.Site!.Name);
        }

        [Fact]
        public async Task A_site_with_its_own_code_or_an_unknown_template_has_no_manifest_from_here()
        {
            tenant = TestHost.Tenant with { Runtime = RuntimeNames.Custom };
            Assert.Null(await Reader().GetAsync());

            cache.Clear();
            tenant = TestHost.Tenant with { TemplateKey = "classic", TemplateVersion = 9 };
            Assert.Null(await Reader().GetAsync());

            cache.Clear();
            tenant = TestHost.Tenant with { TemplateKey = "classic", TemplateVersion = 1 };
            Assert.Null(await new SiteManifestReader(host.Context, Accessor(), cache, Options.Create(new TenantSettingsOptions()), SiteTemplateCatalog.FromJson(Schema)).GetAsync());
        }

        [Theory]
        [InlineData("\"slug\": \"families\", \"name\": \"Families\", \"hasGallery\"", "\"slug\": \"pets\", \"name\": \"Pets\", \"hasGallery\"", "different categories")]
        [InlineData("\"siteDefaultKey\": \"site-default\"", "\"siteDefaultKey\": \"nowhere\"", "isn't one of the manifest's spots")]
        [InlineData("{ \"key\": \"home-hero\", \"label\": \"Hero\", \"page\": \"home\" },", "", "isn't one of the manifest's spots")]
        [InlineData("\"version\": 1,\n  \"site\"", "\"version\": 7,\n  \"site\"", "isn't supported")]
        public void A_template_manifest_with_a_mistake_stops_the_host_with_the_reason(string find, string replace, string expected)
        {
            Assert.Contains(find, Manifest);
            var error = Assert.Throws<InvalidOperationException>(() => SiteTemplateCatalog.FromFiles([(Schema, Manifest.Replace(find, replace))]));
            Assert.Contains("Template classic@1", error.Message);
            Assert.Contains(expected, error.Message);
        }

        [Fact]
        public void The_manifest_is_not_sent_with_the_schema()
        {
            var template = catalog.Find("classic", 1)!;
            Assert.NotNull(template.ManifestJson);
            Assert.DoesNotContain("manifest", JsonSerializer.Serialize(template, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.OrdinalIgnoreCase);
        }

        private IMultiTenantContextAccessor<ElysianTenantInfo> Accessor()
        {
            var accessor = new Mock<IMultiTenantContextAccessor<ElysianTenantInfo>>();
            accessor.Setup(a => a.MultiTenantContext).Returns(() => new MultiTenantContext<ElysianTenantInfo> { TenantInfo = tenant });
            return accessor.Object;
        }

        private SiteManifestReader Reader() => new(host.Context, Accessor(), cache, Options.Create(new TenantSettingsOptions()), catalog);
    }
}
