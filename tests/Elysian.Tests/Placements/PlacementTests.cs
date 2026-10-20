using Elysian.Application.Features.Placements;
using Elysian.Application.Features.Placements.Commands;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Features.Placements.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Elysian.Tests.Placements
{
    public sealed class PlacementTests : IDisposable
    {
        private const string Json = """
            {
              "recipeVersion": 1,
              "siteDefaultKey": "site-default",
              "slots": [
                { "key": "home-hero-1", "name": "home-hero-strip-photo1", "ratio": 0.75, "widths": [480, 720, 1080], "alt": "Hero" },
                { "key": "portfolio-cover-weddings", "name": "portfolio-cover-weddings-photo1", "ratio": 0.8, "widths": [400, 800], "alt": "Weddings", "fallbackCategory": "weddings" },
                { "key": "home-cta", "name": "home-cta-banner-photo1", "ratio": 2.2, "widths": [880, 2400], "alt": "Banner" }
              ]
            }
            """;

        private readonly TestHost host = new();
        private readonly PlacementCatalog catalog = PlacementCatalog.FromJson(Json);
        private readonly IOptions<PhotoStorageSettings> photoSettings = Options.Create(new PhotoStorageSettings());

        // ---- Catalog

        [Fact]
        public void Catalog_reads_slots_and_computes_heights()
        {
            Assert.Equal(3, catalog.Slots.Count);
            var cta = catalog.Slots.Single(s => s.Key == "home-cta");
            Assert.Equal(1091, cta.HeightFor(2400)); // 1090.9 rounds up, as Math.round does in the browser
            Assert.Equal(2400, cta.LargestWidth);
            Assert.Equal("weddings", catalog.Slots[1].FallbackCategory);
            Assert.Null(catalog.Slots[0].FallbackCategory);
        }

        [Theory]
        [InlineData("""{ "siteDefaultKey": "d", "slots": [] }""")]
        [InlineData("""{ "siteDefaultKey": "", "slots": [{ "key": "a", "name": "a", "ratio": 1, "widths": [1], "alt": "" }] }""")]
        [InlineData("""{ "siteDefaultKey": "d", "slots": [{ "key": "a", "name": "a", "ratio": 0, "widths": [1], "alt": "" }] }""")]
        [InlineData("""{ "siteDefaultKey": "d", "slots": [{ "key": "a", "name": "x", "ratio": 1, "widths": [1], "alt": "" }, { "key": "b", "name": "x", "ratio": 1, "widths": [1], "alt": "" }] }""")]
        public void Catalog_rejects_unusable_slots(string json)
        {
            Assert.Throws<InvalidOperationException>(() => PlacementCatalog.FromJson(json));
        }

        // ---- Rendering

        [Theory]
        [InlineData(6000, 4000, 0.75, 0.9, 0.5)]
        [InlineData(6000, 4000, 2.2, 0.0, 1.0)]
        [InlineData(3000, 4500, 0.8, 0.5, 0.05)]
        [InlineData(3000, 4500, 2.2, 1.0, 1.0)]
        public void Crop_has_the_slot_ratio_and_stays_inside_the_image(int width, int height, double ratio, double focusX, double focusY)
        {
            var crop = ImageSharpPlacementRenderer.CropFor(width, height, ratio, focusX, focusY);

            Assert.InRange(crop.X, 0, width - crop.Width);
            Assert.InRange(crop.Y, 0, height - crop.Height);
            Assert.Equal(ratio, (double)crop.Width / crop.Height, 2);
            // Largest possible: one dimension is the whole image
            Assert.True(crop.Width == width || crop.Height == height);
        }

        [Fact]
        public void Crop_centres_on_the_focal_point_when_it_can()
        {
            var crop = ImageSharpPlacementRenderer.CropFor(6000, 4000, 0.75, 0.5, 0.5);
            Assert.InRange(crop.X + crop.Width / 2, 2999, 3001);
        }

        [Fact]
        public async Task Renders_every_width_at_the_slot_ratio_without_upscaling()
        {
            var renderer = new ImageSharpPlacementRenderer(photoSettings);
            var hero = catalog.Slots[0];

            using var large = await renderer.LoadAsync(Png(4000, 3000));
            var variants = await large.RenderAsync(hero, 0.5, 0.5);
            Assert.Equal(hero.Widths, variants.Select(v => v.NamedWidth));
            foreach (var v in variants)
            {
                using var decoded = Image.Load(v.Content);
                Assert.Equal((v.NamedWidth, hero.HeightFor(v.NamedWidth)), (decoded.Width, decoded.Height));
            }

            // 600x500 crops to 375x500 for a 0.75 slot: every file keeps its name but stays 375 wide
            using var small = await renderer.LoadAsync(Png(600, 500));
            var narrow = await small.RenderAsync(hero, 0.5, 0.5);
            Assert.All(narrow, v => Assert.Equal((375, 500), (v.Width, v.Height)));
            Assert.Equal(hero.Widths, narrow.Select(v => v.NamedWidth));
        }

        [Fact]
        public async Task Neutral_frame_has_every_width()
        {
            var variants = await new ImageSharpPlacementRenderer(photoSettings).RenderNeutralAsync(catalog.Slots[2]);
            Assert.Equal(new[] { (880, 400), (2400, 1091) }, variants.Select(v => (v.Width, v.Height)));
        }

        // ---- Resolving what each slot shows

        [Fact]
        public async Task Slots_fall_back_from_explicit_to_category_to_site_default_to_neutral()
        {
            var story = new Story { StoryId = Guid.NewGuid(), Slug = "s", Category = "weddings", Title = "s" };
            host.Context.Stories.Add(story);
            AddPhoto("weddings", sortOrder: 0, status: PhotoStatus.Processing); // not Ready: skipped
            AddPhoto("weddings", sortOrder: 1, storyId: story.StoryId);         // story photo: not in the gallery
            var firstGallery = AddPhoto("weddings", sortOrder: 2);
            AddPhoto("weddings", sortOrder: 3);
            var explicitHero = AddPhoto("site");
            await host.Context.SaveChangesAsync();

            // No site-default yet: the banner has nothing
            Place("home-hero-1", explicitHero);
            await host.Context.SaveChangesAsync();
            var resolved = await Resolver().ResolveAsync(default);

            Assert.Equal((explicitHero.PhotoId, PlacementSource.Explicit), Shown(resolved, "home-hero-1"));
            Assert.Equal((firstGallery.PhotoId, PlacementSource.Category), Shown(resolved, "portfolio-cover-weddings"));
            Assert.Equal(((Guid?)null, PlacementSource.Neutral), Shown(resolved, "home-cta"));

            var fallback = AddPhoto("site");
            Place("site-default", fallback);
            await host.Context.SaveChangesAsync();
            resolved = await Resolver().ResolveAsync(default);

            Assert.Equal((fallback.PhotoId, PlacementSource.SiteDefault), Shown(resolved, "home-cta"));
        }

        [Fact]
        public async Task Fingerprint_changes_with_the_focal_point_and_version_only()
        {
            var photo = AddPhoto("site");
            Place("home-hero-1", photo);
            await host.Context.SaveChangesAsync();

            string Fingerprint(IReadOnlyList<ResolvedSlot> slots) => slots.Single(s => s.Slot.Key == "home-hero-1").Fingerprint;
            var first = Fingerprint(await Resolver().ResolveAsync(default));

            photo.AltText = "New alt text";
            await host.Context.SaveChangesAsync();
            Assert.Equal(first, Fingerprint(await Resolver().ResolveAsync(default)));

            photo.FocusX = 0.2;
            await host.Context.SaveChangesAsync();
            var moved = Fingerprint(await Resolver().ResolveAsync(default));
            Assert.NotEqual(first, moved);

            photo.Version = 2;
            await host.Context.SaveChangesAsync();
            Assert.NotEqual(moved, Fingerprint(await Resolver().ResolveAsync(default)));
        }

        // ---- Publishing

        [Fact]
        public async Task Publish_writes_everything_then_purges_and_queues_the_purges_first()
        {
            var photo = AddPhoto("site");
            Place("home-hero-1", photo);
            await host.Context.SaveChangesAsync();
            var publish = new PublishHarness(this);

            var result = await publish.Handler().Handle(new PublishPlacementsCommand(Message()), default);

            Assert.Equal(PlacementPublishOutcome.Published, result.Outcome);
            Assert.Equal(3, result.ChangedSlots);
            Assert.Equal(7, result.PurgedUrls); // 3 + 2 + 2 widths

            var steps = publish.Steps;
            var firstUpload = steps.FindIndex(s => s.StartsWith("upload"));
            var lastUpload = steps.FindLastIndex(s => s.StartsWith("upload"));
            var purge = steps.IndexOf("purge 7");

            Assert.Equal("send repurge-safety-net 7", steps[steps.FindIndex(s => s.StartsWith("send"))]);
            Assert.True(steps.IndexOf("send repurge-safety-net 7") < firstUpload, "safety net is queued before any write");
            Assert.True(lastUpload < steps.IndexOf("send repurge 7"), "second purge is queued after the writes");
            Assert.True(steps.IndexOf("send repurge 7") < purge, "second purge is queued before the first purge");
            Assert.True(lastUpload < purge, "every file is written before the purge");

            // The largest variant of a slot is written last: it carries the fingerprint the next publish checks
            var heroUploads = steps.Where(s => s.Contains("home-hero-strip-photo1")).ToList();
            Assert.EndsWith("-1080.webp", heroUploads[^1]);
        }

        [Fact]
        public async Task Publish_skips_slots_that_are_already_current()
        {
            var publish = new PublishHarness(this);
            await publish.Handler().Handle(new PublishPlacementsCommand(Message()), default);
            publish.Steps.Clear();

            var result = await publish.Handler().Handle(new PublishPlacementsCommand(Message()), default);

            Assert.Equal(PlacementPublishOutcome.UpToDate, result.Outcome);
            Assert.DoesNotContain(publish.Steps, s => s.StartsWith("upload") || s.StartsWith("purge") || s.StartsWith("send"));
        }

        [Fact]
        public async Task Publish_requeues_when_another_publish_holds_the_lock()
        {
            var publish = new PublishHarness(this) { LockAvailable = false };

            var result = await publish.Handler().Handle(new PublishPlacementsCommand(Message()), default);

            Assert.Equal(PlacementPublishOutcome.Busy, result.Outcome);
            Assert.Equal(["send update 0"], publish.Steps);
        }

        [Fact]
        public async Task Repurge_message_only_purges()
        {
            var publish = new PublishHarness(this);
            var urls = new[] { "https://cdn.example.com/placements/test/a-400.webp" };

            var result = await publish.Handler().Handle(new PublishPlacementsCommand(new("test", "repurge", Repurge: urls)), default);

            Assert.Equal(PlacementPublishOutcome.Repurged, result.Outcome);
            Assert.Equal(["purge 1"], publish.Steps);
        }

        [Fact]
        public async Task Disabled_publishing_does_nothing()
        {
            var publish = new PublishHarness(this) { Enabled = false };

            var result = await publish.Handler().Handle(new PublishPlacementsCommand(Message()), default);

            Assert.Equal(PlacementPublishOutcome.Disabled, result.Outcome);
            Assert.Empty(publish.Steps);
        }

        // ---- Registration

        [Fact]
        public void AddPlacementFeatures_registers_what_the_handlers_need()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureStorageSettings:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["PlacementPublishing:Enabled"] = "true",
                })
                .Build();

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(host.Context)
                .AddSingleton(host.TenantAccessor)
                .AddAzureStorageFeatures(configuration)
                .AddPhotoFeatures(configuration)
                .AddPlacementFeatures(configuration, catalog);

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            // Constructing them resolves every dependency; nothing here touches storage yet (clients are lazy)
            foreach (var handler in new[]
            {
                typeof(PublishPlacementsCommandHandler),
                typeof(RepublishPlacementsCommandHandler),
                typeof(GetPlacementStatusQueryHandler),
            })
            {
                Assert.NotNull(ActivatorUtilities.CreateInstance(scope.ServiceProvider, handler));
            }
        }

        // ---- Helpers

        private PlacementResolver Resolver() => new(host.Context, catalog, photoSettings);

        private static PlacementPublishMessage Message() => new(TestHost.Tenant.Identifier!, "update");

        private static (Guid?, PlacementSource) Shown(IReadOnlyList<ResolvedSlot> slots, string key)
        {
            var slot = slots.Single(s => s.Slot.Key == key);
            return (slot.Photo?.PhotoId, slot.Source);
        }

        private Photo AddPhoto(string category, int sortOrder = 0, PhotoStatus status = PhotoStatus.Ready, Guid? storyId = null)
        {
            var photoId = Guid.NewGuid();
            var photo = new Photo
            {
                PhotoId = photoId,
                Category = category,
                StoryId = storyId,
                SortOrder = sortOrder,
                OriginalFileName = "photo.jpg",
                OriginalBlobName = $"test/{photoId}/original.png",
                OriginalFileSize = 1000,
                Width = 1200,
                Height = 1600,
                Version = 1,
                VariantWidths = [400, 800],
                Status = status,
            };
            host.Context.Photos.Add(photo);
            return photo;
        }

        private void Place(string key, Photo photo) => host.Context.Placements.Add(new Placement { Key = key, PhotoId = photo.PhotoId });

        private static MemoryStream Png(int width, int height)
        {
            using var image = new Image<Rgb24>(width, height, new Rgb24(120, 90, 60));
            var stream = new MemoryStream();
            image.SaveAsPng(stream);
            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// Real resolver and renderer; in-memory store, queue and CDN that record each step in order
        /// </summary>
        private sealed class PublishHarness(PlacementTests tests)
        {
            private readonly Dictionary<string, PlacementMetadata> files = [];

            public List<string> Steps { get; } = [];
            public bool LockAvailable { get; init; } = true;
            public bool Enabled { get; init; } = true;

            public PublishPlacementsCommandHandler Handler()
            {
                var store = new Mock<IPlacementStore>();
                store.Setup(s => s.GetPublicUrl(It.IsAny<string>())).Returns<string>(path => $"https://cdn.example.com/placements/{path}");
                store.Setup(s => s.GetPublishedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string path, CancellationToken _) =>
                        files.TryGetValue(path, out var m) ? new PublishedPlacement(m.Fingerprint, m.Source.ToString(), m.PublishedAt) : null);
                store.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<PlacementMetadata>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string path, byte[] _, PlacementMetadata metadata, CancellationToken _) =>
                    {
                        files[path] = metadata;
                        Steps.Add($"upload {path}");
                        return "\"etag\"";
                    });
                var publishLock = new Mock<IPlacementLock>();
                store.Setup(s => s.TryAcquireLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(LockAvailable ? publishLock.Object : null);

                var purger = new Mock<ICdnPurger>();
                purger.SetupGet(p => p.IsConfigured).Returns(true);
                purger.Setup(p => p.PurgeAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                    .Callback((IReadOnlyCollection<string> urls, CancellationToken _) => Steps.Add($"purge {urls.Count}"))
                    .Returns(Task.CompletedTask);
                purger.Setup(p => p.FindStaleAsync(It.IsAny<IReadOnlyList<PublishedFile>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);

                var queue = new Mock<IPlacementPublishQueue>();
                queue.Setup(q => q.SendAsync(It.IsAny<PlacementPublishMessage>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                    .Callback((PlacementPublishMessage m, TimeSpan? _, CancellationToken _) => Steps.Add($"send {m.Reason} {m.Repurge?.Length ?? 0}"))
                    .Returns(Task.CompletedTask);

                var photoStorage = new Mock<IPhotoStorage>();
                photoStorage.Setup(s => s.OpenOriginalAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => Png(1200, 1600));

                return new PublishPlacementsCommandHandler(
                    tests.Resolver(),
                    new ImageSharpPlacementRenderer(tests.photoSettings),
                    store.Object,
                    purger.Object,
                    queue.Object,
                    photoStorage.Object,
                    Options.Create(new PlacementPublishingSettings { Enabled = Enabled }),
                    tests.host.TenantAccessor,
                    NullLogger<PublishPlacementsCommandHandler>.Instance);
            }
        }

        public void Dispose() => host.Dispose();
    }
}
