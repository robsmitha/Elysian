using Elysian.Application.Exceptions;
using Elysian.Application.Features.Placements;
using Elysian.Application.Features.Placements.Commands;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Features.SiteContent;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
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
    /// <summary>
    /// Moving a site from one shared template to another: the new look's images first, the switch second
    /// </summary>
    public sealed class SiteTemplateChangeTests : IDisposable
    {
        private static string Schema(string key, string name, string palette, string font) => $$"""
            {
              "key": "{{key}}", "version": 1, "name": "{{name}}", "description": "How {{name}} looks.",
              "allowCustomAccent": true,
              "palettes": [
                { "key": "{{palette}}", "name": "First", "background": "#ffffff", "surface": "#f3f5f7", "text": "#16181d", "muted": "#4a5160", "accent": "#1f5f4f", "onAccent": "#ffffff" },
                { "key": "night", "name": "Night", "background": "#101214", "surface": "#181b1f", "text": "#f2f3f5", "muted": "#b3b9c2", "accent": "#e8b84a", "onAccent": "#101214" }
              ],
              "fonts": [ { "key": "{{font}}", "name": "First" }, { "key": "shared", "name": "Shared" } ],
              "fields": [ { "key": "home.heading", "label": "Heading", "group": "Home", "maxLength": 60, "required": true, "default": "Hello" } ],
              "links": [ "instagram" ],
              "categories": [ { "slug": "weddings", "name": "Weddings" } ]
            }
            """;

        private static string Manifest(string prefix, double ratio) => $$"""
            {
              "version": 1,
              "site": { "name": "", "publicUrl": "" },
              "categories": [ { "slug": "weddings", "name": "Weddings", "hasGallery": true, "hasStories": false } ],
              "pages": [ { "key": "home", "name": "Home", "path": "/" }, { "key": "site", "name": "Whole site", "path": "/" } ],
              "spots": [ { "key": "home-hero", "label": "Hero", "page": "home" }, { "key": "site-default", "label": "Fallback", "page": "site" } ],
              "placements": {
                "recipeVersion": 1,
                "siteDefaultKey": "site-default",
                "slots": [ { "key": "home-hero", "name": "{{prefix}}home-hero", "ratio": {{ratio}}, "widths": [320, 640], "alt": "Featured" } ]
              }
            }
            """;

        private static readonly SiteTemplateCatalog Catalog = SiteTemplateCatalog.FromFiles([
            (Schema("classic", "Classic", "linen", "serif"), Manifest("", 2.2)),
            (Schema("gallery", "Gallery", "paper", "sans"), Manifest("g-", 0.8)),
            // A template with no image slots of its own can't be moved to
            (Schema("plain", "Plain", "paper", "sans"), null),
        ]);

        private readonly TestHost host = new();
        private readonly PlatformContext platform;
        private readonly ElysianTenantInfo tenant = TestHost.Tenant with { Runtime = RuntimeNames.Cloud, TemplateKey = "classic", TemplateVersion = 1 };
        private readonly List<PlacementPublishMessage> queued = [];
        private readonly List<string> steps = [];
        private bool edgeFails;

        public SiteTemplateChangeTests()
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

        private IMultiTenantContextAccessor<ElysianTenantInfo> Accessor()
        {
            var accessor = new Mock<IMultiTenantContextAccessor<ElysianTenantInfo>>();
            accessor.Setup(a => a.MultiTenantContext).Returns(() => new MultiTenantContext<ElysianTenantInfo> { TenantInfo = tenant });
            return accessor.Object;
        }

        private IPlacementPublishQueue Queue()
        {
            var queue = new Mock<IPlacementPublishQueue>();
            queue.Setup(q => q.SendAsync(It.IsAny<PlacementPublishMessage>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback((PlacementPublishMessage m, TimeSpan? _, CancellationToken _) => { queued.Add(m); steps.Add($"queue {m.Reason}"); })
                .Returns(Task.CompletedTask);
            return queue.Object;
        }

        private SiteTemplateChanger Changer(bool withQueue = true)
        {
            var hostMap = new Mock<IEdgeHostMap>();
            hostMap.Setup(m => m.SyncAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string identifier, CancellationToken _) =>
                {
                    if (edgeFails)
                    {
                        throw new InvalidOperationException("The edge could not be reached.");
                    }
                    // What the edge is told is whatever the tenant's record says at that moment
                    var row = await platform.Tenants.AsNoTracking().SingleAsync();
                    steps.Add($"edge {identifier} {row.TemplateKey}@{row.TemplateVersion}");
                });
            return new SiteTemplateChanger(host.Context, platform, Catalog, Accessor(), hostMap: hostMap.Object, queue: withQueue ? Queue() : null);
        }

        private SiteContentReader Reader() => new(host.Context, Catalog, Accessor());

        private Task<SiteTemplateChangeModel> ChangeAsync(string? template, bool withQueue = true) =>
            new ChangeSiteTemplateCommandHandler(Reader(), Catalog, Changer(withQueue)).Handle(new ChangeSiteTemplateCommand(template), default);

        private async Task SaveDraftAsync(SiteContentDocument content)
        {
            host.Context.SiteContentDrafts.Add(new SiteContentDraft { Json = content.ToJson(), TemplateKey = "classic", TemplateVersion = 1 });
            await host.Context.SaveChangesAsync();
        }

        // ---- Asking

        [Fact]
        public async Task The_templates_on_offer_are_the_ones_with_their_own_image_slots()
        {
            var options = Changer().Options();

            Assert.Equal([("classic", true), ("gallery", false)], options.Select(o => (o.Key, o.Current)));
            Assert.Equal("How Gallery looks.", options[1].Description);
        }

        [Fact]
        public async Task Asking_for_a_change_records_it_and_queues_the_new_templates_images_and_changes_nothing_else()
        {
            var change = await ChangeAsync(" Gallery ");

            Assert.Equal(("gallery", 1, "Gallery"), (change.TemplateKey, change.TemplateVersion, change.TemplateName));
            var message = Assert.Single(queued);
            Assert.Equal((tenant.Identifier, SiteTemplateChanger.PublishReason, "gallery", 1), (message.Tenant, message.Reason, message.TemplateKey, message.TemplateVersion));

            // Still on the old template, and the editing screen can say a change is under way
            Assert.Equal("classic", (await platform.Tenants.AsNoTracking().SingleAsync()).TemplateKey);
            Assert.Equal("gallery", (await Changer().PendingAsync(default))!.TemplateKey);
            var model = await new GetSiteContentQueryHandler(host.Context, Reader(), Changer()).Handle(new GetSiteContentQuery(), default);
            Assert.Equal("classic", model.Template.Key);
            Assert.Equal("gallery", model.TemplateChange!.TemplateKey);
            Assert.Equal(2, model.Templates!.Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("nope")]
        [InlineData("plain")]
        public async Task A_template_that_cannot_be_moved_to_is_refused(string? template)
        {
            await Assert.ThrowsAsync<CustomValidationException>(() => ChangeAsync(template));
            Assert.Empty(queued);
            Assert.Null(await Changer().PendingAsync(default));
        }

        [Fact]
        public async Task The_template_a_site_is_already_on_is_refused_and_so_is_a_host_that_cannot_render()
        {
            await Assert.ThrowsAsync<ConflictException>(() => ChangeAsync("classic"));
            await Assert.ThrowsAsync<ConflictException>(() => ChangeAsync("gallery", withQueue: false));
            Assert.Null(await Changer().PendingAsync(default));
        }

        [Fact]
        public async Task A_change_can_be_given_up_before_it_finishes()
        {
            await ChangeAsync("gallery");
            await new CancelSiteTemplateChangeCommandHandler(Reader(), Changer()).Handle(new CancelSiteTemplateChangeCommand(), default);

            Assert.Null(await Changer().PendingAsync(default));
            // The images may still be rendered, but the site is not moved
            Assert.False(await Changer().CompleteAsync("gallery", 1, default));
            Assert.Equal("classic", (await platform.Tenants.AsNoTracking().SingleAsync()).TemplateKey);
        }

        // ---- Finishing

        [Fact]
        public async Task Finishing_moves_the_draft_then_the_record_then_the_edge()
        {
            await SaveDraftAsync(new SiteContentDocument
            {
                Theme = new SiteContentTheme("linen", "#1f5f4f", "shared"),
                Text = new Dictionary<string, string> { ["home.heading"] = "What I wrote" },
                Links = new Dictionary<string, string> { ["instagram"] = "https://www.instagram.com/example" },
                Categories = [new SiteContentCategory("weddings", "Elopements")],
            });
            await ChangeAsync("gallery");
            steps.Clear();

            Assert.True(await Changer().CompleteAsync("gallery", 1, default));

            var row = await platform.Tenants.AsNoTracking().SingleAsync();
            Assert.Equal(("gallery", 1), (row.TemplateKey, row.TemplateVersion));
            // The edge is told only once the record says gallery, and a follow-up publish catches late picks
            Assert.Equal(["edge test gallery@1", $"queue {SiteTemplateChanger.SettledReason}"], steps);
            Assert.Null(await Changer().PendingAsync(default));

            var draft = await host.Context.SiteContentDrafts.AsNoTracking().SingleAsync();
            Assert.Equal(("gallery", 1), (draft.TemplateKey, draft.TemplateVersion));
            var content = SiteContentDocument.FromJson(draft.Json);
            // Wording, links and category names are kept; a palette the new template lacks becomes its first,
            // and the accent chosen against it goes too; a font both have is kept
            Assert.Equal("What I wrote", content.Text["home.heading"]);
            Assert.Equal("https://www.instagram.com/example", content.Links["instagram"]);
            Assert.Equal("Elopements", content.Categories[0].Name);
            Assert.Equal(new SiteContentTheme("paper", null, "shared"), content.Theme);
            Assert.Empty(content.Validate(Catalog.Find("gallery", 1)!));
        }

        [Fact]
        public void A_palette_both_templates_have_keeps_its_accent_when_it_still_reads()
        {
            var gallery = Catalog.Find("gallery", 1)!;
            var kept = SiteTemplateChanger.Fitted(new SiteContentDocument { Theme = new SiteContentTheme("night", "#e8b84a", "serif") }, gallery);
            Assert.Equal(new SiteContentTheme("night", "#e8b84a", "sans"), kept.Theme);

            // An accent that can't be read on the palette doesn't come along
            var dropped = SiteTemplateChanger.Fitted(new SiteContentDocument { Theme = new SiteContentTheme("night", "#101214", "shared") }, gallery);
            Assert.Equal(new SiteContentTheme("night", null, "shared"), dropped.Theme);
        }

        [Fact]
        public async Task When_the_edge_cannot_be_told_the_change_stays_under_way_and_finishing_again_completes_it()
        {
            await ChangeAsync("gallery");
            edgeFails = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => Changer().CompleteAsync("gallery", 1, default));
            Assert.NotNull(await Changer().PendingAsync(default));

            edgeFails = false;
            Assert.True(await Changer().CompleteAsync("gallery", 1, default));
            Assert.Null(await Changer().PendingAsync(default));
            Assert.Equal("gallery", (await platform.Tenants.AsNoTracking().SingleAsync()).TemplateKey);
        }

        [Fact]
        public async Task Only_the_change_that_was_asked_for_last_is_finished()
        {
            await ChangeAsync("gallery");
            Assert.False(await Changer().CompleteAsync("classic", 1, default));
            Assert.False(await Changer().CompleteAsync("gallery", 7, default));
            Assert.Equal("classic", (await platform.Tenants.AsNoTracking().SingleAsync()).TemplateKey);
        }

        // ---- The whole way: the queue message renders the new template's images, then the site moves

        [Fact]
        public async Task The_new_templates_images_are_all_written_before_the_site_is_pointed_at_it()
        {
            await ChangeAsync("gallery");
            steps.Clear();

            var result = await PublishHandler().Handle(new PublishPlacementsCommand(queued[0]), default);

            Assert.Equal(PlacementPublishOutcome.Published, result.Outcome);
            // The gallery template's own files, never the classic ones the site is still showing
            var uploads = steps.Where(s => s.StartsWith("upload")).ToList();
            Assert.Equal(["upload test/g-home-hero-320.webp", "upload test/g-home-hero-640.webp"], uploads);
            Assert.True(steps.IndexOf("edge test gallery@1") > steps.IndexOf(uploads[^1]), string.Join(" | ", steps));
            Assert.Equal("gallery", (await platform.Tenants.AsNoTracking().SingleAsync()).TemplateKey);
        }

        [Fact]
        public async Task A_site_moving_back_to_images_it_already_has_still_moves()
        {
            await ChangeAsync("gallery");
            var handler = PublishHandler();
            await handler.Handle(new PublishPlacementsCommand(queued[0]), default);

            // Asked for again by a retry of the same message: nothing to render, and the site is where it should be
            steps.Clear();
            var again = await handler.Handle(new PublishPlacementsCommand(queued[0]), default);
            Assert.Equal(PlacementPublishOutcome.UpToDate, again.Outcome);
            Assert.DoesNotContain(steps, s => s.StartsWith("upload"));
            Assert.Equal("gallery", (await platform.Tenants.AsNoTracking().SingleAsync()).TemplateKey);
        }

        [Fact]
        public async Task An_ordinary_publish_while_a_change_is_under_way_leaves_the_site_where_it_is()
        {
            await ChangeAsync("gallery");
            steps.Clear();

            await PublishHandler().Handle(new PublishPlacementsCommand(new PlacementPublishMessage(tenant.Identifier!, "update-photo")), default);

            Assert.Contains("upload test/home-hero-640.webp", steps);
            Assert.DoesNotContain(steps, s => s.StartsWith("edge"));
            Assert.Equal("classic", (await platform.Tenants.AsNoTracking().SingleAsync()).TemplateKey);
            Assert.NotNull(await Changer().PendingAsync(default));
        }

        private readonly Dictionary<string, PlacementMetadata> files = [];

        private PublishPlacementsCommandHandler PublishHandler()
        {
            var photoSettings = Options.Create(new PhotoStorageSettings());

            var store = new Mock<IPlacementStore>();
            store.Setup(s => s.GetPublicUrl(It.IsAny<string>())).Returns<string>(path => $"https://cdn.example.com/placements/{path}");
            store.Setup(s => s.GetPublishedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, CancellationToken _) =>
                    files.TryGetValue(path, out var m) ? new PublishedPlacement(m.Fingerprint, m.Source.ToString(), m.PublishedAt) : null);
            store.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<PlacementMetadata>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, byte[] _, PlacementMetadata metadata, CancellationToken _) =>
                {
                    files[path] = metadata;
                    steps.Add($"upload {path}");
                    return "\"etag\"";
                });
            store.Setup(s => s.TryAcquireLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Mock<IPlacementLock>().Object);

            var purger = new Mock<ICdnPurger>();
            purger.SetupGet(p => p.IsConfigured).Returns(false);

            // The tenant's own catalog is the template it is on now
            var current = new Mock<IPlacementCatalogSource>();
            current.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Changer().CatalogFor(platform.Tenants.AsNoTracking().Single().TemplateKey!, 1));

            return new PublishPlacementsCommandHandler(
                current.Object,
                new PlacementResolver(host.Context, photoSettings),
                new ImageSharpPlacementRenderer(photoSettings),
                store.Object,
                purger.Object,
                Queue(),
                new Mock<IPhotoStorage>().Object,
                Options.Create(new PlacementPublishingSettings { Enabled = true }),
                Accessor(),
                NullLogger<PublishPlacementsCommandHandler>.Instance,
                Changer());
        }
    }
}
