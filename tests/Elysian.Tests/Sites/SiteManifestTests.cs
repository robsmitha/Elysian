using System.Reflection;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Placements;
using Elysian.Application.Features.Sites;
using Elysian.Application.Features.Sites.Commands;
using Elysian.Application.Features.Sites.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Elysian.Tests.Sites
{
    public sealed class SiteManifestTests : IDisposable
    {
        private const string Manifest = """
            {
              "version": 1,
              "site": { "name": "Test Photography", "publicUrl": "https://www.example.com" },
              "categories": [
                { "slug": "weddings", "name": "Weddings", "hasStories": true },
                { "slug": "events", "name": "Events", "hasGallery": false }
              ],
              "placements": {
                "recipeVersion": 1,
                "siteDefaultKey": "site-default",
                "slots": [
                  { "key": "home-hero-1", "name": "home-hero-strip-photo1", "ratio": 0.75, "widths": [480, 1080], "alt": "Hero" },
                  { "key": "portfolio-cover-weddings", "name": "portfolio-cover-weddings-photo1", "ratio": 0.8, "widths": [400, 800], "alt": "Weddings", "fallbackCategory": "weddings" }
                ]
              },
              "spots": [
                { "key": "home-hero-1", "label": "Hero strip, photo 1", "page": "home" },
                { "key": "home-story", "label": "Story portrait", "page": "home" },
                { "key": "portfolio-cover-weddings", "label": "Weddings tile cover", "page": "portfolio" },
                { "key": "site-default", "label": "Fallback for any empty spot", "page": "site" }
              ],
              "pages": [
                { "key": "home", "name": "Home", "path": "/", "layout": [
                  { "type": "text" },
                  { "type": "row", "children": [ { "type": "spot", "spot": "home-hero-1", "ratio": 0.75 }, { "type": "lines", "count": 3 } ] },
                  { "type": "spot", "spot": "home-story", "ratio": 0.8 }
                ] },
                { "key": "portfolio", "name": "Portfolio", "path": "/portfolio", "layout": [
                  { "type": "grid", "columns": 3, "children": [ { "type": "spot", "spot": "portfolio-cover-weddings" } ] }
                ] },
                { "key": "site", "name": "Whole site", "path": "/" }
              ]
            }
            """;

        private const string HostCatalog = """
            {
              "recipeVersion": 1,
              "siteDefaultKey": "site-default",
              "slots": [ { "key": "host-slot", "name": "host-slot-photo1", "ratio": 1, "widths": [400], "alt": "Host" } ]
            }
            """;

        private readonly TestHost host = new();
        private readonly MemoryCache cache = new(new MemoryCacheOptions());
        private readonly List<string> queued = [];

        // ---- The document

        [Fact]
        public void Manifest_reads_the_site_its_categories_slots_and_pages()
        {
            var manifest = SiteManifestDocument.FromJson(Manifest);

            Assert.Equal("Test Photography", manifest.Site!.Name);
            Assert.Equal("https://www.example.com", manifest.Site.PublicUrl);

            Assert.Equal(["weddings", "events"], manifest.Categories.Select(c => c.Slug));
            Assert.True(manifest.Categories[0].HasGallery);
            Assert.True(manifest.Categories[0].HasStories);
            Assert.False(manifest.Categories[1].HasGallery);

            var hero = manifest.Placements!.Slots[0];
            Assert.Equal(("home-hero-1", "home-hero-strip-photo1"), (hero.Key, hero.Name));
            Assert.Equal(640, hero.HeightFor(480));
            Assert.Equal("weddings", manifest.Placements.Slots[1].FallbackCategory);

            // Every spot can be picked for; only some are published as reserved images
            Assert.Equal(["home-hero-1", "home-story", "portfolio-cover-weddings", "site-default"], manifest.Spots.Select(s => s.Key));
            Assert.Equal(("Story portrait", "home"), (manifest.Spots[1].Label, manifest.Spots[1].Page));
            Assert.DoesNotContain(manifest.Placements.Slots, s => s.Key == "home-story");

            Assert.Equal(["home", "portfolio", "site"], manifest.Pages.Select(p => p.Key));
            Assert.Equal(3, manifest.Pages[0].Layout!.Value.GetArrayLength());
            Assert.Null(manifest.Pages[2].Layout);
        }

        [Fact]
        public void A_site_with_no_featured_spots_needs_no_placements()
        {
            var manifest = SiteManifestDocument.FromJson("""{ "site": { "name": "Plain", "publicUrl": "https://plain.example.com" } }""");

            Assert.Null(manifest.Placements);
            Assert.Empty(manifest.Categories);
            Assert.Empty(manifest.Pages);
        }

        [Theory]
        [InlineData("", "empty")]
        [InlineData("{ not json", "valid JSON")]
        [InlineData("""{ "version": 2, "site": { "name": "A", "publicUrl": "https://a.example.com" } }""", "version 2")]
        [InlineData("""{ "site": { "name": " ", "publicUrl": "https://a.example.com" } }""", "site.name")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "http://a.example.com" } }""", "https")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "/relative" } }""", "https")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "categories": [ { "slug": "a", "name": "A" }, { "slug": "a", "name": "Again" } ] }""", "category a twice")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "pages": [ { "key": "home", "name": "Home", "path": "/" }, { "key": "home", "name": "Home", "path": "/" } ] }""", "page home twice")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "placements": { "siteDefaultKey": "d", "slots": [] } }""", "needs slots")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "pages": [ { "key": "home", "name": "Home", "path": "/" } ], "spots": [ { "key": "a", "label": "A", "page": "missing" } ] }""", "page missing")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "pages": [ { "key": "home", "name": "Home", "path": "/" } ], "spots": [ { "key": "a", "label": "", "page": "home" } ] }""", "key and a label")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "pages": [ { "key": "home", "name": "Home", "path": "/" } ], "spots": [ { "key": "a", "label": "A", "page": "home" }, { "key": "a", "label": "Again", "page": "home" } ] }""", "spot a twice")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "pages": [ { "key": "home", "name": "Home", "path": "/" } ], "spots": [ { "key": "d", "label": "Default", "page": "home" } ], "placements": { "siteDefaultKey": "d", "slots": [ { "key": "a", "name": "a", "ratio": 1, "widths": [1], "alt": "" } ] } }""", "slot a isn't one of")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "pages": [ { "key": "home", "name": "Home", "path": "/" } ], "spots": [ { "key": "a", "label": "A", "page": "home" } ], "placements": { "siteDefaultKey": "d", "slots": [ { "key": "a", "name": "a", "ratio": 1, "widths": [1], "alt": "" } ] } }""", "site default d")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "categories": [ { "slug": "weddings", "name": "Weddings" } ], "pages": [ { "key": "home", "name": "Home", "path": "/" } ], "spots": [ { "key": "a", "label": "A", "page": "home" }, { "key": "d", "label": "Default", "page": "home" } ], "placements": { "siteDefaultKey": "d", "slots": [ { "key": "a", "name": "a", "ratio": 1, "widths": [1], "alt": "", "fallbackCategory": "family" } ] } }""", "category family")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "pages": [ { "key": "home", "name": "Home", "path": "/", "layout": { "type": "row" } } ] }""", "list of blocks")]
        [InlineData("""{ "site": { "name": "A", "publicUrl": "https://a.example.com" }, "pages": [ { "key": "home", "name": "Home", "path": "/", "layout": [ { "type": "row", "children": [ { "type": "spot", "spot": "nowhere" } ] } ] } ] }""", "spot nowhere")]
        public void Manifest_rejects_what_it_cant_use(string json, string expectedInMessage)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => SiteManifestDocument.FromJson(json));
            Assert.Contains(expectedInMessage, ex.Message);
        }

        [Fact]
        public void Manifest_rejects_an_oversized_document()
        {
            var padding = new string('x', SiteManifestDocument.MaxLength);
            var json = "{ \"site\": { \"name\": \"" + padding + "\", \"publicUrl\": \"https://a.example.com\" } }";

            var ex = Assert.Throws<InvalidOperationException>(() => SiteManifestDocument.FromJson(json));
            Assert.Contains("larger than", ex.Message);
        }

        // ---- Reading the stored copy

        [Fact]
        public async Task A_tenant_with_no_manifest_reads_as_none()
        {
            Assert.Null(await Reader().GetAsync());
        }

        [Fact]
        public async Task A_stored_manifest_is_read_with_its_json()
        {
            await SeedAsync(Manifest);

            var stored = await Reader().GetAsync();

            Assert.NotNull(stored);
            Assert.Equal("Test Photography", stored.Document.Site!.Name);
            Assert.Equal(Manifest, stored.Json);
            Assert.Equal("seed", stored.Source);
        }

        [Fact]
        public async Task A_stored_manifest_that_cant_be_read_throws_instead_of_counting_as_none()
        {
            await SeedAsync("""{ "site": { "name": "", "publicUrl": "https://a.example.com" } }""");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Reader().GetAsync());
            Assert.Contains("can't be used", ex.Message);

            // And so the publisher never falls back to the host's catalog for it
            await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogSource(PlacementCatalog.FromJson(HostCatalog)).GetAsync());
        }

        [Fact]
        public async Task The_manifest_is_served_from_memory_until_it_is_invalidated()
        {
            var reader = Reader();
            Assert.Null(await reader.GetAsync());

            await SeedAsync(Manifest);
            Assert.Null(await Reader().GetAsync());

            reader.Invalidate();
            Assert.NotNull(await Reader().GetAsync());
        }

        // ---- Which slots a tenant publishes

        [Fact]
        public async Task A_tenant_with_a_manifest_uses_its_slots_not_the_hosts()
        {
            await SeedAsync(Manifest);

            var catalog = await CatalogSource(PlacementCatalog.FromJson(HostCatalog)).GetAsync();

            Assert.Equal(["home-hero-1", "portfolio-cover-weddings"], catalog!.Slots.Select(s => s.Key));
        }

        [Fact]
        public async Task A_tenant_with_no_manifest_uses_the_hosts_catalog_when_there_is_one()
        {
            var catalog = await CatalogSource(PlacementCatalog.FromJson(HostCatalog)).GetAsync();
            Assert.Equal(["host-slot"], catalog!.Slots.Select(s => s.Key));
        }

        [Fact]
        public async Task A_tenant_with_no_manifest_on_a_shared_host_has_no_slots()
        {
            Assert.Null(await CatalogSource(null).GetAsync());
        }

        [Fact]
        public async Task A_manifest_without_placements_means_no_slots_even_when_the_host_has_a_catalog()
        {
            await SeedAsync("""{ "site": { "name": "Plain", "publicUrl": "https://plain.example.com" } }""");

            Assert.Null(await CatalogSource(PlacementCatalog.FromJson(HostCatalog)).GetAsync());
        }

        // ---- Storing

        [Fact]
        public async Task Storing_the_first_manifest_saves_it_and_queues_a_publish()
        {
            var result = await Store().Handle(new StoreSiteManifestCommand(Manifest, "deploy"), default);

            Assert.Empty(result.RemovedSlots);
            Assert.True(result.PlacementsChanged);
            Assert.Equal(["site-manifest"], queued);

            var row = await host.Context.SiteManifests.SingleAsync();
            Assert.Equal((Manifest, "deploy"), (row.Json, row.Source));
            Assert.NotNull(await Reader().GetAsync());
        }

        [Fact]
        public async Task Storing_again_replaces_the_one_row_and_is_seen_at_once()
        {
            var reader = Reader();
            await Store().Handle(new StoreSiteManifestCommand(Manifest), default);
            Assert.Equal("Test Photography", (await reader.GetAsync())!.Document.Site!.Name);

            await Store().Handle(new StoreSiteManifestCommand(Manifest.Replace("Test Photography", "Renamed Studio")), default);

            Assert.Equal(1, await host.Context.SiteManifests.CountAsync());
            Assert.Equal("Renamed Studio", (await Reader().GetAsync())!.Document.Site!.Name);
        }

        [Fact]
        public async Task A_change_that_leaves_the_slots_alone_queues_no_publish()
        {
            await Store().Handle(new StoreSiteManifestCommand(Manifest), default);
            queued.Clear();

            var relabelled = Manifest.Replace("Hero strip, photo 1", "First hero photo").Replace("Test Photography", "Renamed Studio");
            var result = await Store().Handle(new StoreSiteManifestCommand(relabelled), default);

            Assert.False(result.PlacementsChanged);
            Assert.Empty(queued);
        }

        [Theory]
        [InlineData("\"widths\": [480, 1080]", "\"widths\": [480, 1440]")]
        [InlineData("\"ratio\": 0.75, \"widths\"", "\"ratio\": 0.8, \"widths\"")]
        [InlineData("\"recipeVersion\": 1", "\"recipeVersion\": 2")]
        public async Task A_change_to_what_gets_rendered_queues_a_publish(string from, string to)
        {
            await Store().Handle(new StoreSiteManifestCommand(Manifest), default);
            queued.Clear();
            Assert.Contains(from, Manifest);

            var result = await Store().Handle(new StoreSiteManifestCommand(Manifest.Replace(from, to)), default);

            Assert.True(result.PlacementsChanged);
            Assert.Equal(["site-manifest"], queued);
        }

        [Fact]
        public async Task A_manifest_that_drops_a_published_image_name_is_refused()
        {
            await Store().Handle(new StoreSiteManifestCommand(Manifest), default);
            queued.Clear();

            // The same slot under a new public name: pages still point at the old one
            var renamed = Manifest.Replace("home-hero-strip-photo1", "home-hero-photo-one");
            var ex = await Assert.ThrowsAsync<CustomValidationException>(() => Store().Handle(new StoreSiteManifestCommand(renamed), default));

            Assert.Contains("home-hero-strip-photo1", ex.Errors[nameof(StoreSiteManifestCommand.Json)].Single());
            Assert.Equal(Manifest, (await host.Context.SiteManifests.SingleAsync()).Json);
            Assert.Empty(queued);
        }

        [Fact]
        public async Task Dropping_a_published_image_name_on_purpose_is_allowed_and_reported()
        {
            await Store().Handle(new StoreSiteManifestCommand(Manifest), default);

            var renamed = Manifest.Replace("home-hero-strip-photo1", "home-hero-photo-one");
            var result = await Store().Handle(new StoreSiteManifestCommand(renamed, AllowRemovedSlots: true), default);

            Assert.Equal(["home-hero-strip-photo1"], result.RemovedSlots);
            Assert.True(result.PlacementsChanged);
            Assert.Equal(renamed, (await host.Context.SiteManifests.SingleAsync()).Json);
        }

        [Fact]
        public async Task A_manifest_that_cant_be_used_is_refused_and_nothing_is_stored()
        {
            var ex = await Assert.ThrowsAsync<CustomValidationException>(() =>
                Store().Handle(new StoreSiteManifestCommand("""{ "site": { "name": "A", "publicUrl": "http://a.example.com" } }"""), default));

            Assert.Contains("https", ex.Errors[nameof(StoreSiteManifestCommand.Json)].Single());
            Assert.Empty(await host.Context.SiteManifests.ToListAsync());
            Assert.Empty(queued);
        }

        [Theory]
        [InlineData("")]
        [InlineData("a-source-name-that-is-much-too-long-to-store")]
        public async Task A_manifest_needs_a_short_source(string source)
        {
            var ex = await Assert.ThrowsAsync<CustomValidationException>(() => Store().Handle(new StoreSiteManifestCommand(Manifest, source), default));
            Assert.True(ex.Errors.ContainsKey(nameof(StoreSiteManifestCommand.Source)));
        }

        [Fact]
        public async Task An_unreadable_stored_manifest_can_be_replaced()
        {
            await SeedAsync("{ broken");

            var result = await Store().Handle(new StoreSiteManifestCommand(Manifest), default);

            Assert.Empty(result.RemovedSlots);
            Assert.Equal(Manifest, (await host.Context.SiteManifests.SingleAsync()).Json);
        }

        [Fact]
        public async Task Storing_works_on_a_host_without_placement_publishing()
        {
            var handler = new StoreSiteManifestCommandHandler(host.Context, Reader(), NullLogger<StoreSiteManifestCommandHandler>.Instance);

            var result = await handler.Handle(new StoreSiteManifestCommand(Manifest), default);

            Assert.True(result.PlacementsChanged);
        }

        [Fact]
        public void The_manifest_json_stays_out_of_request_logging()
        {
            var logged = new StoreSiteManifestCommand(Manifest, "deploy").ToString();

            Assert.DoesNotContain("Test Photography", logged);
            Assert.Contains("deploy", logged);
        }

        // ---- What the admin app reads

        [Fact]
        public async Task Site_query_returns_the_tenant_its_modules_and_the_manifest_as_written()
        {
            await SeedAsync(Manifest);

            var site = await SiteQuery("photos", "booking").Handle(new GetSiteQuery(), default);

            Assert.Equal(TestHost.Tenant.Identifier, site.Identifier);
            Assert.Equal(TestHost.Tenant.Name, site.Name);
            Assert.Equal(["booking", "photos"], site.Modules);
            Assert.NotNull(site.ManifestStoredAt);
            Assert.Equal("Test Photography", site.Manifest!.Value.GetProperty("site").GetProperty("name").GetString());
            // Page layouts pass through untouched for the admin app to draw
            Assert.Equal("row", site.Manifest.Value.GetProperty("pages")[0].GetProperty("layout")[1].GetProperty("type").GetString());
            Assert.Equal(4, site.Manifest.Value.GetProperty("spots").GetArrayLength());
        }

        [Fact]
        public async Task Site_query_works_for_a_tenant_with_no_manifest()
        {
            var site = await SiteQuery().Handle(new GetSiteQuery(), default);

            Assert.Null(site.Manifest);
            Assert.Null(site.ManifestStoredAt);
            Assert.Empty(site.Modules);
        }

        [Fact]
        public void Only_members_read_a_site_and_only_platform_admins_store_its_manifest()
        {
            Assert.NotNull(typeof(GetSiteQuery).GetCustomAttribute<AuthorizeAttribute>());
            Assert.NotNull(typeof(StoreSiteManifestCommand).GetCustomAttribute<RequiresPlatformAdminAttribute>());
        }

        // ---- Helpers

        private SiteManifestReader Reader() =>
            new(host.Context, host.TenantAccessor, cache, Options.Create(new TenantSettingsOptions()));

        private TenantPlacementCatalogSource CatalogSource(PlacementCatalog? hostCatalog) =>
            new(Reader(), new HostPlacementCatalog(hostCatalog));

        private StoreSiteManifestCommandHandler Store()
        {
            var queue = new Mock<IPlacementPublishQueue>();
            queue.Setup(q => q.EnqueueAsync(It.IsAny<string>(), It.IsAny<bool>()))
                .Callback((string reason, bool _) => queued.Add(reason))
                .Returns(Task.CompletedTask);

            return new StoreSiteManifestCommandHandler(host.Context, Reader(), NullLogger<StoreSiteManifestCommandHandler>.Instance, queue.Object);
        }

        private GetSiteQueryHandler SiteQuery(params string[] activeModules)
        {
            var modules = new Mock<ITenantModuleService>();
            modules.Setup(m => m.GetActiveModulesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeModules.ToHashSet());

            return new GetSiteQueryHandler(Reader(), modules.Object, host.TenantAccessor);
        }

        private async Task SeedAsync(string json)
        {
            host.Context.SiteManifests.Add(new SiteManifest { Json = json, Source = "seed" });
            await host.Context.SaveChangesAsync();
        }

        public void Dispose()
        {
            cache.Dispose();
            host.Dispose();
        }
    }
}
