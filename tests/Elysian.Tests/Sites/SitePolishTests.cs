using Elysian.Application.Exceptions;
using Elysian.Application.Features.Photos;
using Elysian.Application.Features.Photos.Commands;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Features.Platform.Commands;
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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Elysian.Tests.Sites
{
    /// <summary>
    /// A site's logo, going back to an earlier published version, and how much a site may upload
    /// </summary>
    public sealed class SitePolishTests : IDisposable
    {
        private const string Schema = """
            {
              "key": "classic", "version": 1, "name": "Classic",
              "palettes": [ { "key": "paper", "name": "Paper", "background": "#ffffff", "surface": "#f3f5f7", "text": "#16181d", "muted": "#4a5160", "accent": "#1f5f4f", "onAccent": "#ffffff" } ],
              "fonts": [ { "key": "sans", "name": "Modern sans" } ],
              "fields": [ { "key": "home.heading", "label": "Home heading", "group": "Home", "maxLength": 60, "required": true, "default": "Hello" } ],
              "links": [ "instagram" ],
              "categories": [ { "slug": "weddings", "name": "Weddings" } ]
            }
            """;

        private readonly TestHost host = new();
        private readonly PlatformContext platform;
        private readonly SiteTemplateCatalog catalog = new([SiteTemplate.FromJson(Schema), SiteTemplate.FromJson(Schema.Replace("\"version\": 1", "\"version\": 2"))]);
        private readonly Clock clock = new(DateTimeOffset.Parse("2026-11-23T12:00:00Z"));
        private readonly Dictionary<string, byte[]> stored = [];
        private ElysianTenantInfo tenant = TestHost.Tenant with { TemplateKey = "classic", TemplateVersion = 1, Runtime = RuntimeNames.Cloud };

        private sealed class Clock(DateTimeOffset now) : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = now;
            public override DateTimeOffset GetUtcNow() => Now;
        }

        public SitePolishTests()
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

        private SiteContentReader Reader() => new(host.Context, catalog, Accessor());

        private static SiteContentDocument Content(string heading, SiteContentLogo? logo = null) => new()
        {
            Theme = new SiteContentTheme("paper", null, "sans"),
            Text = new Dictionary<string, string> { ["home.heading"] = heading },
            Logo = logo,
        };

        private Task<SiteContentDocument> SaveAsync(SiteContentDocument content) =>
            new SaveSiteContentDraftCommandHandler(host.Context, Reader()).Handle(new SaveSiteContentDraftCommand(content), default);

        private async Task<int> PublishAsync()
        {
            var edge = new Mock<ISiteContentEdge>();
            var published = await new PublishSiteContentCommandHandler(host.Context, platform, Reader(), edge.Object, NullLogger<PublishSiteContentCommandHandler>.Instance)
                .Handle(new PublishSiteContentCommand(), default);
            return published.Version;
        }

        private async Task<SiteContentDocument> DraftAsync() =>
            SiteContentDocument.FromJson((await host.Context.SiteContentDrafts.AsNoTracking().SingleAsync()).Json);

        // ---- Going back to an earlier version

        private Task<List<SiteContentVersionSummary>> VersionsAsync() =>
            new GetSiteContentVersionsQueryHandler(host.Context, Reader()).Handle(new GetSiteContentVersionsQuery(), default);

        private Task<SiteContentDocument> RestoreAsync(int version) =>
            new RestoreSiteContentVersionCommandHandler(host.Context, Reader()).Handle(new RestoreSiteContentVersionCommand(version), default);

        [Fact]
        public async Task The_versions_are_listed_newest_first_with_the_live_one_marked()
        {
            Assert.Empty(await VersionsAsync());

            await SaveAsync(Content("First"));
            await PublishAsync();
            await SaveAsync(Content("Second"));
            await PublishAsync();

            var versions = await VersionsAsync();
            Assert.Equal([2, 1], versions.Select(v => v.Version));
            Assert.Equal([true, false], versions.Select(v => v.IsLive));
            Assert.All(versions, v => Assert.True(v.CanRestore));
        }

        [Fact]
        public async Task Going_back_makes_the_earlier_version_the_draft_and_changes_nothing_visitors_see()
        {
            await SaveAsync(Content("First"));
            await PublishAsync();
            await SaveAsync(Content("Second"));
            await PublishAsync();
            await SaveAsync(Content("Half-written third"));

            var restored = await RestoreAsync(1);

            Assert.Equal("First", restored.Text["home.heading"]);
            Assert.Equal("First", (await DraftAsync()).Text["home.heading"]);
            // Nothing was published: version 2 is still the newest, and the site still points at it
            Assert.Equal(2, await host.Context.SiteContentVersions.MaxAsync(v => v.Version));
            Assert.Equal(2, (await platform.Tenants.AsNoTracking().SingleAsync()).ContentVersion);

            // Publishing it is an ordinary publish, and a new version
            Assert.Equal(3, await PublishAsync());
        }

        [Fact]
        public async Task A_version_that_is_not_there_cannot_be_gone_back_to()
        {
            await Assert.ThrowsAsync<NotFoundException>(() => RestoreAsync(7));
        }

        [Fact]
        public async Task A_version_made_with_another_template_is_listed_but_cannot_be_gone_back_to()
        {
            await SaveAsync(Content("First"));
            await PublishAsync();
            tenant = tenant with { TemplateVersion = 2 };

            Assert.False(Assert.Single(await VersionsAsync()).CanRestore);
            await Assert.ThrowsAsync<ConflictException>(() => RestoreAsync(1));
        }

        // ---- The logo

        private IPlacementStore Store()
        {
            var store = new Mock<IPlacementStore>();
            store.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<PlacementMetadata>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, byte[] content, PlacementMetadata _, CancellationToken _) =>
                {
                    stored[path] = content;
                    return "etag";
                });
            return store.Object;
        }

        private static byte[] Png(int width, int height, bool transparent = true)
        {
            using var image = new Image<Rgba32>(width, height, transparent ? new Rgba32(0, 0, 0, 0) : new Rgba32(255, 255, 255, 255));
            for (var x = 0; x < width / 2; x++)
            {
                // A solid left half, so the edge of what is drawn survives being made smaller
                for (var y = 0; y < height; y++)
                {
                    image[x, y] = new Rgba32(200, 30, 30, 255);
                }
            }
            using var output = new MemoryStream();
            image.SaveAsPng(output);
            return output.ToArray();
        }

        private Task<SiteContentDocument> UploadLogoAsync(byte[]? upload, bool withStore = true) =>
            new UploadSiteLogoCommandHandler(host.Context, Reader(), new ImageSharpSiteLogoRenderer(), clock, withStore ? Store() : null)
                .Handle(new UploadSiteLogoCommand(upload), default);

        [Theory]
        [InlineData(1200, 400, 480, 160)]   // Scaled down to the tallest a header shows
        [InlineData(4000, 400, 800, 80)]    // A wide one is held to the widest
        [InlineData(300, 101, 300, 100)]    // Never enlarged, and an even height
        [InlineData(90, 60, 90, 60)]
        public void A_logo_is_made_to_fit_a_header_and_never_enlarged(int sourceWidth, int sourceHeight, int width, int height)
        {
            Assert.Equal((width, height), ImageSharpSiteLogoRenderer.Fit(sourceWidth, sourceHeight));
        }

        [Fact]
        public async Task An_uploaded_logo_is_drawn_again_as_two_files_and_set_in_the_draft()
        {
            await SaveAsync(Content("Hello"));

            var content = await UploadLogoAsync(Png(1200, 400));

            var version = clock.Now.ToUnixTimeSeconds();
            Assert.Equal(new SiteContentLogo(version, 480, 160), content.Logo);
            Assert.Equal(content.Logo, (await DraftAsync()).Logo);
            Assert.Equal("Hello", (await DraftAsync()).Text["home.heading"]);
            Assert.Equal([$"test/logo-{version}-160.webp", $"test/logo-{version}-80.webp"], stored.Keys.Order());

            // WebP, at the sizes the page is told, with the transparency kept
            using var large = Image.Load<Rgba32>(stored[$"test/logo-{version}-160.webp"]);
            using var small = Image.Load<Rgba32>(stored[$"test/logo-{version}-80.webp"]);
            Assert.Equal((480, 160, 240, 80), (large.Width, large.Height, small.Width, small.Height));
            Assert.Equal("Webp", Image.DetectFormat(stored[$"test/logo-{version}-160.webp"]).Name, ignoreCase: true);
            Assert.Equal(0, large[470, 10].A);
            Assert.Equal(255, large[10, 80].A);
        }

        [Fact]
        public async Task A_site_nobody_has_edited_yet_can_be_given_a_logo()
        {
            var content = await UploadLogoAsync(Png(200, 100));

            Assert.NotNull(content.Logo);
            var row = await host.Context.SiteContentDrafts.AsNoTracking().SingleAsync();
            Assert.Equal(("classic", 1), (row.TemplateKey, row.TemplateVersion));
        }

        [Fact]
        public async Task What_is_not_a_usable_image_is_refused_and_nothing_is_stored()
        {
            var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
            foreach (var bad in new byte[]?[] { null, [], svg, [1, 2, 3, 4], Png(200, 20), Png(4000, 100), new byte[SiteLogoPaths.MaxUploadBytes + 1] })
            {
                await Assert.ThrowsAsync<CustomValidationException>(() => UploadLogoAsync(bad));
            }
            Assert.Empty(stored);
            Assert.Empty(host.Context.SiteContentDrafts);
        }

        [Fact]
        public async Task A_host_that_cannot_store_images_says_so()
        {
            await Assert.ThrowsAsync<ConflictException>(() => UploadLogoAsync(Png(200, 100), withStore: false));
        }

        [Fact]
        public async Task Saving_the_draft_keeps_the_logo_whatever_the_screen_sends()
        {
            var logo = (await UploadLogoAsync(Png(200, 100))).Logo!;

            // Sent without one, and sent with one that was never uploaded
            Assert.Equal(logo, (await SaveAsync(Content("Hello"))).Logo);
            Assert.Equal(logo, (await SaveAsync(Content("Hello again", new SiteContentLogo(1, 9999, 9999)))).Logo);
            Assert.Equal(logo, (await DraftAsync()).Logo);
        }

        [Fact]
        public async Task Removing_the_logo_takes_it_out_of_the_draft_and_leaves_the_files()
        {
            await SaveAsync(Content("Hello"));
            await UploadLogoAsync(Png(200, 100));

            var content = await new RemoveSiteLogoCommandHandler(host.Context, Reader()).Handle(new RemoveSiteLogoCommand(), default);

            Assert.Null(content.Logo);
            Assert.Null((await DraftAsync()).Logo);
            Assert.Equal("Hello", (await DraftAsync()).Text["home.heading"]);
            Assert.Equal(2, stored.Count);
        }

        [Fact]
        public async Task A_new_logo_shows_in_the_draft_while_visitors_keep_the_published_one_and_going_back_restores_it()
        {
            var first = (await UploadLogoAsync(Png(200, 100))).Logo!;
            await PublishAsync();

            clock.Now = clock.Now.AddMinutes(5);
            var second = (await UploadLogoAsync(Png(300, 100))).Logo!;

            Assert.NotEqual(first.Version, second.Version);
            // Four files: the published logo's two were not touched
            Assert.Equal(4, stored.Count);
            var published = SiteContentDocument.FromJson((await host.Context.SiteContentVersions.AsNoTracking().SingleAsync()).Json);
            Assert.Equal(first, published.Logo);

            await PublishAsync();
            Assert.Equal(first, (await RestoreAsync(1)).Logo);
        }

        [Fact]
        public void Content_without_a_logo_is_written_exactly_as_it_was_before_there_were_logos()
        {
            var json = Content("Hello").Normalize().ToJson();
            Assert.DoesNotContain("logo", json);
            Assert.Null(SiteContentDocument.FromJson(json).Logo);

            var withLogo = Content("Hello", new SiteContentLogo(1764000000, 480, 160)).Normalize();
            Assert.Equal(withLogo.Logo, SiteContentDocument.FromJson(withLogo.ToJson()).Logo);
        }

        [Theory]
        [InlineData(0, 480, 160)]
        [InlineData(1764000000, 480, 161)]
        [InlineData(1764000000, 480, 20)]
        [InlineData(1764000000, 480, 400)]
        [InlineData(1764000000, 0, 160)]
        [InlineData(1764000000, 5000, 160)]
        public void A_logo_that_could_not_have_been_made_here_is_not_stored(long version, int width, int height)
        {
            var problems = Content("Hello", new SiteContentLogo(version, width, height)).Normalize().Validate(catalog.Find("classic", 1)!);
            Assert.Contains(problems, p => p.Field == "logo");
        }

        // ---- How much a site may upload

        private StorageQuota Quota(int defaultGb = 25) =>
            new(host.Context, Accessor(), Options.Create(new PlatformStorageSettings { DefaultQuotaGb = defaultGb }));

        private void AddPhoto(long bytes)
        {
            var id = Guid.NewGuid();
            host.Context.Photos.Add(new Photo
            {
                PhotoId = id, Category = "weddings", OriginalFileName = "a.jpg", OriginalBlobName = $"test/{id}/original.jpg", OriginalFileSize = bytes,
            });
            host.Context.SaveChanges();
        }

        private Task<CreatePhotoUploadResponse> UploadPhotoAsync(long bytes, int defaultGb = 25)
        {
            var storage = new Mock<IPhotoStorage>();
            storage.Setup(s => s.CreateOriginalUploadUriAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Uri("https://storage.example.com/upload"));
            return new CreatePhotoUploadCommandHandler(host.Context, storage.Object, Accessor(), Quota(defaultGb))
                .Handle(new CreatePhotoUploadCommand("b.jpg", bytes, "weddings", null), default);
        }

        [Fact]
        public async Task A_site_is_told_how_much_it_has_used_of_what_it_may()
        {
            AddPhoto(3 * StorageQuota.Gigabyte);
            AddPhoto(StorageQuota.Gigabyte / 2);

            var usage = await new GetStorageUsageQueryHandler(Quota()).Handle(new GetStorageUsageQuery(), default);

            Assert.Equal(new StorageUsageModel(3 * StorageQuota.Gigabyte + StorageQuota.Gigabyte / 2, 25 * StorageQuota.Gigabyte, 2), usage);
        }

        [Fact]
        public async Task A_photo_that_fits_is_taken_and_one_that_would_go_over_is_refused_before_anything_is_recorded()
        {
            AddPhoto(StorageQuota.Gigabyte - 100);

            await UploadPhotoAsync(100, defaultGb: 1);
            Assert.Equal(2, await host.Context.Photos.CountAsync());

            var error = await Assert.ThrowsAsync<ConflictException>(() => UploadPhotoAsync(1, defaultGb: 1));
            Assert.Contains("1 GB of photo storage", error.Message);
            Assert.Equal(2, await host.Context.Photos.CountAsync());
        }

        [Fact]
        public async Task A_site_with_a_limit_of_its_own_is_held_to_that()
        {
            AddPhoto(StorageQuota.Gigabyte);

            await new SetTenantStorageQuotaCommandHandler(platform).Handle(new SetTenantStorageQuotaCommand("test", 2), default);
            tenant = (await platform.Tenants.AsNoTracking().SingleAsync()) with { };
            Assert.Equal(2, tenant.StorageQuotaGb);
            Assert.Equal(2 * StorageQuota.Gigabyte, Quota(defaultGb: 1).LimitBytes());
            await UploadPhotoAsync(100, defaultGb: 1);

            // And back to the platform's
            await new SetTenantStorageQuotaCommandHandler(platform).Handle(new SetTenantStorageQuotaCommand("test", null), default);
            tenant = (await platform.Tenants.AsNoTracking().SingleAsync()) with { };
            Assert.Equal(StorageQuota.Gigabyte, Quota(defaultGb: 1).LimitBytes());
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData(1, true)]
        [InlineData(StorageQuota.MostGb, true)]
        [InlineData(0, false)]
        [InlineData(-5, false)]
        [InlineData(StorageQuota.MostGb + 1, false)]
        public void A_limit_is_a_sensible_number_of_gigabytes_or_none(int? gigabytes, bool valid)
        {
            Assert.Equal(valid, new SetTenantStorageQuotaCommandValidator().Validate(new SetTenantStorageQuotaCommand("test", gigabytes)).IsValid);
        }

        [Fact]
        public async Task A_site_with_its_own_code_and_a_platform_with_no_limit_are_never_refused()
        {
            AddPhoto(500 * StorageQuota.Gigabyte);

            Assert.Null(Quota(defaultGb: 0).LimitBytes());
            await UploadPhotoAsync(100, defaultGb: 0);

            tenant = tenant with { Runtime = RuntimeNames.Custom, StorageQuotaGb = 1 };
            Assert.Null(Quota().LimitBytes());
            await UploadPhotoAsync(100);
        }

        [Theory]
        [InlineData(0L, "0 MB")]
        [InlineData(325L * 1024 * 1024, "325 MB")]
        [InlineData(25L * 1024 * 1024 * 1024, "25 GB")]
        [InlineData(26_414_000_000L, "24.6 GB")]
        public void Sizes_are_said_the_way_a_person_would(long bytes, string shown)
        {
            Assert.Equal(shown, StorageQuota.Shown(bytes));
        }
    }
}
