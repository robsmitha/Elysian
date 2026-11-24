using Elysian.Application.Exceptions;
using Elysian.Application.Features.Platform;
using Elysian.Application.Features.Platform.Commands;
using Elysian.Application.Features.SiteContent;
using Elysian.Application.Features.Sites;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
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

namespace Elysian.Tests.Platforms
{
    /// <summary>
    /// Deleting a site for good: two deliberate steps, some days to change one's mind, then the edge, its
    /// files and its rows, in that order
    /// </summary>
    public sealed class TenantDeletionTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly PlatformContext platform;
        private readonly List<string> steps = [];
        private readonly List<TenantJobMessage> queued = [];
        private readonly Clock clock = new(DateTimeOffset.Parse("2026-11-23T12:00:00Z"));

        private sealed class Clock(DateTimeOffset now) : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = now;
            public override DateTimeOffset GetUtcNow() => Now;
        }
        private ElysianTenantInfo tenant = TestHost.Tenant with { Runtime = RuntimeNames.Cloud, Status = TenantStatusNames.Suspended, TemplateKey = "classic", TemplateVersion = 1 };

        public TenantDeletionTests()
        {
            platform = new PlatformContext(new DbContextOptionsBuilder<PlatformContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            platform.Tenants.Add(tenant with { });
            platform.TenantDomains.Add(PlatformContext.Stamp(new TenantDomain
            {
                TenantId = tenant.Id!, Host = "test.framenice.example", Kind = TenantDomainKinds.Subdomain, Status = TenantDomainStatuses.Active, IsPrimary = true,
            }, "seed", DateTimeOffset.UtcNow));
            platform.SaveChanges();
        }

        public void Dispose()
        {
            platform.Dispose();
            host.Dispose();
        }

        private async Task SetAsync(Action<ElysianTenantInfo> change)
        {
            var row = await platform.Tenants.SingleAsync();
            change(row);
            await platform.SaveChangesAsync();
            tenant = row with { };
        }

        private IEdgeHostMap HostMap()
        {
            var map = new Mock<IEdgeHostMap>();
            map.Setup(m => m.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string h, CancellationToken _) => steps.Add($"edge host {h}"))
                .Returns(Task.CompletedTask);
            map.Setup(m => m.SyncAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string t, CancellationToken _) => steps.Add($"edge sync {t}"))
                .Returns(Task.CompletedTask);
            return map.Object;
        }

        private ITenantJobQueue Queue(bool fails = false)
        {
            var queue = new Mock<ITenantJobQueue>();
            queue.Setup(q => q.EnqueueAsync(It.IsAny<TenantJobMessage>(), It.IsAny<CancellationToken>()))
                .Returns((TenantJobMessage m, CancellationToken _) =>
                {
                    if (fails)
                    {
                        throw new InvalidOperationException("The queue could not be reached.");
                    }
                    queued.Add(m);
                    steps.Add($"queue {m.Job}");
                    return Task.CompletedTask;
                });
            return queue.Object;
        }

        private Task DeleteAsync(string? confirm = "test", bool queueFails = false, bool now = false, int days = 14) =>
            new DeleteTenantCommandHandler(platform, Queue(queueFails), HostMap(), null,
                    Options.Create(new PlatformDeletionSettings { GraceDays = days }), clock)
                .Handle(new DeleteTenantCommand("test", confirm, now), default);

        private Task RestoreAsync() =>
            new RestoreDeletedTenantCommandHandler(platform, HostMap(), null, clock).Handle(new RestoreDeletedTenantCommand("test"), default);

        private Task<int> QueueDueAsync() =>
            new EnqueueDueSiteDeletionsCommandHandler(platform, Queue(), NullLogger<EnqueueDueSiteDeletionsCommandHandler>.Instance, clock)
                .Handle(new EnqueueDueSiteDeletionsCommand(), default);

        // ---- Asking

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("tes")]
        [InlineData("another-site")]
        public void The_sites_name_has_to_be_typed_again(string? confirm)
        {
            var result = new DeleteTenantCommandValidator().Validate(new DeleteTenantCommand("test", confirm));
            Assert.Contains(result.Errors, e => e.ErrorMessage == "Type the site's name exactly to confirm.");
            Assert.True(new DeleteTenantCommandValidator().Validate(new DeleteTenantCommand("test", " Test ")).IsValid);
        }

        [Theory]
        [InlineData(TenantStatusNames.Active)]
        [InlineData(TenantStatusNames.Trial)]
        public async Task A_site_that_is_not_suspended_cannot_be_deleted(string status)
        {
            await SetAsync(t => t.Status = status);

            var error = await Assert.ThrowsAsync<ConflictException>(() => DeleteAsync());
            Assert.Contains("Suspend the site first", error.Message);
            Assert.Equal(status, (await platform.Tenants.AsNoTracking().SingleAsync()).Status);
            Assert.Empty(steps);
        }

        [Fact]
        public async Task A_site_with_its_own_code_cannot_be_deleted_here()
        {
            await SetAsync(t => t.Runtime = RuntimeNames.Custom);

            await Assert.ThrowsAsync<ConflictException>(() => DeleteAsync());
            Assert.Equal(TenantStatusNames.Suspended, (await platform.Tenants.AsNoTracking().SingleAsync()).Status);
            Assert.Empty(steps);
        }

        [Fact]
        public async Task Deleting_stops_the_site_answering_and_keeps_everything_for_some_days()
        {
            await DeleteAsync();

            var row = await platform.Tenants.AsNoTracking().SingleAsync();
            Assert.Equal(TenantStatusNames.Deleting, row.Status);
            Assert.Equal(clock.Now.AddDays(14), row.DeleteAfter);
            // It stops answering, and that is all: nothing is queued, so nothing is removed
            Assert.Equal(["edge host test.framenice.example"], steps);
            Assert.Empty(queued);
            Assert.Equal(row.DeleteAfter, (await new GetTenantsQueryHandler(platform).Handle(new GetTenantsQuery(), default)).Single().DeleteAfter);
        }

        [Fact]
        public async Task Asking_again_does_not_start_the_days_over_and_removes_nothing_early()
        {
            await DeleteAsync();
            var due = (await platform.Tenants.AsNoTracking().SingleAsync()).DeleteAfter;

            clock.Now = clock.Now.AddDays(3);
            await DeleteAsync();

            Assert.Equal(due, (await platform.Tenants.AsNoTracking().SingleAsync()).DeleteAfter);
            Assert.Empty(queued);

            // Unless it is asked to go now
            await DeleteAsync(now: true);
            Assert.Equal(clock.Now, (await platform.Tenants.AsNoTracking().SingleAsync()).DeleteAfter);
            Assert.Single(queued);
        }

        [Fact]
        public async Task With_no_days_to_wait_everything_goes_at_once()
        {
            await DeleteAsync(days: 0);

            Assert.Equal(clock.Now, (await platform.Tenants.AsNoTracking().SingleAsync()).DeleteAfter);
            Assert.Single(queued);
        }

        [Theory]
        [InlineData(-5, 0)]
        [InlineData(14, 14)]
        [InlineData(5000, PlatformDeletionSettings.MostDays)]
        public void The_days_are_kept_within_reason(int configured, int days)
        {
            Assert.Equal(days, new PlatformDeletionSettings { GraceDays = configured }.Days);
        }

        [Fact]
        public async Task A_deleted_site_can_be_brought_back_whole_while_its_days_last()
        {
            await DeleteAsync();
            steps.Clear();
            clock.Now = clock.Now.AddDays(13);

            await RestoreAsync();

            var row = await platform.Tenants.AsNoTracking().SingleAsync();
            // Suspended, as it was before it was deleted; restoring it from there is the usual step
            Assert.Equal(TenantStatusNames.Suspended, row.Status);
            Assert.Null(row.DeleteAfter);
            Assert.Equal(["edge sync test"], steps);
            Assert.Single(platform.TenantDomains);

            // And it is no longer due, however long passes
            clock.Now = clock.Now.AddDays(30);
            Assert.Equal(0, await QueueDueAsync());
        }

        [Fact]
        public async Task A_site_cannot_be_brought_back_once_its_days_have_run_out()
        {
            await DeleteAsync();
            clock.Now = clock.Now.AddDays(14);

            var error = await Assert.ThrowsAsync<ConflictException>(RestoreAsync);
            Assert.Contains("too late", error.Message);
            Assert.Equal(TenantStatusNames.Deleting, (await platform.Tenants.AsNoTracking().SingleAsync()).Status);
        }

        [Fact]
        public async Task A_site_that_is_not_being_deleted_has_nothing_to_bring_back()
        {
            await Assert.ThrowsAsync<ConflictException>(RestoreAsync);
            Assert.Equal(TenantStatusNames.Suspended, (await platform.Tenants.AsNoTracking().SingleAsync()).Status);
        }

        [Fact]
        public async Task The_daily_run_queues_only_sites_whose_days_have_run_out()
        {
            await DeleteAsync();

            clock.Now = clock.Now.AddDays(13);
            Assert.Equal(0, await QueueDueAsync());
            Assert.Empty(queued);

            clock.Now = clock.Now.AddDays(1);
            Assert.Equal(1, await QueueDueAsync());
            Assert.Equal(new TenantJobMessage("test", TenantJobCatalog.DeleteSite), Assert.Single(queued));

            // Still there the next day (the job failed partway): queued again
            Assert.Equal(1, await QueueDueAsync());
        }

        [Fact]
        public async Task The_daily_run_leaves_live_sites_and_sites_with_their_own_code_alone()
        {
            Assert.Equal(0, await QueueDueAsync());

            await SetAsync(t => { t.Status = TenantStatusNames.Deleting; t.Runtime = RuntimeNames.Custom; });
            Assert.Equal(0, await QueueDueAsync());

            // One marked before there were days to wait has no date, and is due
            await SetAsync(t => t.Runtime = RuntimeNames.Cloud);
            Assert.Equal(1, await QueueDueAsync());
        }

        [Fact]
        public async Task Deleting_now_marks_the_site_stops_it_answering_and_queues_the_rest()
        {
            await DeleteAsync(now: true);

            Assert.Equal(TenantStatusNames.Deleting, (await platform.Tenants.AsNoTracking().SingleAsync()).Status);
            Assert.Equal(["edge host test.framenice.example", $"queue {TenantJobCatalog.DeleteSite}"], steps);
            Assert.Equal("test", Assert.Single(queued).Tenant);
            // Nothing is removed here: the job does that
            Assert.Single(platform.TenantDomains);
        }

        [Fact]
        public async Task A_delete_that_did_not_finish_can_be_asked_for_again()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => DeleteAsync(queueFails: true, now: true));
            Assert.Equal(TenantStatusNames.Deleting, (await platform.Tenants.AsNoTracking().SingleAsync()).Status);

            await DeleteAsync();
            Assert.Single(queued);
        }

        [Fact]
        public async Task A_site_being_deleted_cannot_be_brought_back_by_changing_its_status()
        {
            await DeleteAsync();

            await Assert.ThrowsAsync<ConflictException>(() =>
                new SetTenantStatusCommandHandler(platform).Handle(new SetTenantStatusCommand("test", TenantStatusNames.Active), default));
            Assert.Equal(TenantStatusNames.Deleting, (await platform.Tenants.AsNoTracking().SingleAsync()).Status);
        }

        [Fact]
        public async Task The_job_is_never_queued_for_every_site()
        {
            var definition = TenantJobCatalog.Find(TenantJobCatalog.DeleteSite)!;
            Assert.True(definition.OnRequest);
            Assert.IsType<PurgeSiteCommand>(definition.CreateRequest());

            var store = new Mock<IMultiTenantStore<ElysianTenantInfo>>();
            var handler = new EnqueueTenantJobsCommandHandler(platform, store.Object, Queue(), NullLogger<EnqueueTenantJobsCommandHandler>.Instance);
            await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new EnqueueTenantJobsCommand(TenantJobCatalog.DeleteSite), default));
            Assert.Empty(queued);
        }

        // ---- The job

        private sealed class RecordingEraser(List<string> steps) : ITenantDataEraser
        {
            public Task<int> EraseAsync(string tenantId, string tenantIdentifier, CancellationToken cancellationToken = default)
            {
                steps.Add($"rows {tenantId} {tenantIdentifier}");
                return Task.FromResult(42);
            }
        }

        private PurgeSiteCommandHandler Purge(bool purgeFails = false)
        {
            var accessor = new Mock<IMultiTenantContextAccessor<ElysianTenantInfo>>();
            accessor.Setup(a => a.MultiTenantContext).Returns(() => new MultiTenantContext<ElysianTenantInfo> { TenantInfo = tenant });

            var edge = new Mock<IEdgeConfigStore>();
            edge.SetupGet(e => e.IsConfigured).Returns(true);
            edge.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string key, CancellationToken _) => steps.Add($"edge {key}"))
                .Returns(Task.CompletedTask);

            var photos = new Mock<IPhotoStorage>();
            photos.Setup(p => p.ListVariantsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string prefix, CancellationToken _) => new HashSet<string> { $"{prefix}a/v1/400.webp", $"{prefix}a/v1/800.webp" });
            photos.Setup(p => p.GetPublicUrl(It.IsAny<string>())).Returns<string>(path => $"https://images.example.com/photos/{path}");
            photos.Setup(p => p.DeleteVariantsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string prefix, CancellationToken _) => steps.Add($"photos {prefix}"))
                .Returns(Task.CompletedTask);
            photos.Setup(p => p.DeleteOriginalsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string prefix, CancellationToken _) => steps.Add($"originals {prefix}"))
                .ReturnsAsync(1);

            var placements = new Mock<IPlacementStore>();
            placements.Setup(p => p.GetPublicUrl(It.IsAny<string>())).Returns<string>(path => $"https://images.example.com/placements/{path}");
            placements.Setup(p => p.DeleteAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string prefix, CancellationToken _) => steps.Add($"placements {prefix}"))
                .ReturnsAsync((string prefix, CancellationToken _) => [$"{prefix}home-hero-640.webp"]);

            var purger = new Mock<ICdnPurger>();
            purger.SetupGet(p => p.IsConfigured).Returns(true);
            purger.Setup(p => p.PurgeAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyCollection<string> urls, CancellationToken _) =>
                {
                    if (purgeFails)
                    {
                        throw new InvalidOperationException("Cloudflare refused.");
                    }
                    steps.Add($"purge {string.Join(" ", urls.Order())}");
                    return Task.CompletedTask;
                });

            return new PurgeSiteCommandHandler(host.Context, platform, new RecordingEraser(steps), accessor.Object,
                NullLogger<PurgeSiteCommandHandler>.Instance, edge.Object, HostMap(), photos.Object, placements.Object, purger.Object, clock);
        }

        [Fact]
        public async Task The_job_touches_nothing_of_a_site_that_can_still_be_brought_back()
        {
            await DeleteAsync();
            steps.Clear();
            await SetAsync(_ => { });
            clock.Now = clock.Now.AddDays(13);

            Assert.Equal(PurgeSiteCommandHandler.NotDue, await Purge().Handle(new PurgeSiteCommand(), default));
            Assert.Empty(steps);

            clock.Now = clock.Now.AddDays(1);
            Assert.StartsWith("Deleted test", await Purge().Handle(new PurgeSiteCommand(), default));
        }

        [Theory]
        [InlineData(TenantStatusNames.Active)]
        [InlineData(TenantStatusNames.Suspended)]
        public async Task The_job_touches_nothing_of_a_site_that_is_not_marked_as_being_deleted(string status)
        {
            await SetAsync(t => t.Status = status);

            Assert.Equal(PurgeSiteCommandHandler.NotMarked, await Purge().Handle(new PurgeSiteCommand(), default));
            Assert.Empty(steps);
        }

        [Fact]
        public async Task The_job_touches_nothing_of_a_site_with_its_own_code_however_it_is_marked()
        {
            await SetAsync(t => { t.Status = TenantStatusNames.Deleting; t.Runtime = RuntimeNames.Custom; });

            Assert.Equal(PurgeSiteCommandHandler.NotMarked, await Purge().Handle(new PurgeSiteCommand(), default));
            Assert.Empty(steps);
        }

        [Fact]
        public async Task The_edge_goes_first_then_the_files_then_the_rows()
        {
            host.Context.SiteContentVersions.Add(new SiteContentVersion { Version = 1, Json = "{}", TemplateKey = "classic", TemplateVersion = 1 });
            host.Context.SiteContentVersions.Add(new SiteContentVersion { Version = 2, Json = "{}", TemplateKey = "classic", TemplateVersion = 1 });
            await host.Context.SaveChangesAsync();
            await SetAsync(t => t.Status = TenantStatusNames.Deleting);

            var result = await Purge().Handle(new PurgeSiteCommand(), default);

            Assert.Equal(
            [
                "edge host test.framenice.example",
                "edge content:test:1",
                "edge content:test:2",
                // One site's folder exactly: the slash keeps "test" from matching "test-two"
                "photos test/",
                "originals test/",
                "placements test/",
                "purge https://images.example.com/photos/test/a/v1/400.webp https://images.example.com/photos/test/a/v1/800.webp https://images.example.com/placements/test/home-hero-640.webp",
                "rows tenant-1 test",
            ], steps);
            Assert.Equal("Deleted test: 4 files, 42 rows", result);
        }

        [Fact]
        public async Task A_cdn_that_cannot_be_purged_does_not_keep_the_rows()
        {
            await SetAsync(t => t.Status = TenantStatusNames.Deleting);

            await Purge(purgeFails: true).Handle(new PurgeSiteCommand(), default);

            Assert.Equal("rows tenant-1 test", steps[^1]);
        }

        [Fact]
        public async Task A_job_for_a_site_that_is_already_gone_does_nothing()
        {
            platform.Tenants.Remove(await platform.Tenants.SingleAsync());
            await platform.SaveChangesAsync();

            Assert.Equal("Already gone", await Purge().Handle(new PurgeSiteCommand(), default));
            Assert.Empty(steps);
        }

        // ---- The rows

        [Fact]
        public void Every_table_that_holds_a_tenants_rows_is_cleared_and_the_tenant_itself_goes_last()
        {
            // The list is the one row-level security is held to, so a new tenant table can't be missed here
            foreach (var table in RowLevelSecurity.ProtectedTables)
            {
                Assert.Equal($"DELETE FROM [{table}] WHERE [TenantId] = @id", SqlTenantDataEraser.TenantStatement(table));
            }
            Assert.Contains("Photo", RowLevelSecurity.ProtectedTables);
            Assert.Contains("TenantDomain", RowLevelSecurity.ProtectedTables);

            Assert.StartsWith("DELETE FROM [TenantInfo]", SqlTenantDataEraser.PlatformStatements[^1]);
            // Never by a pattern, and never without saying which tenant
            Assert.All(SqlTenantDataEraser.PlatformStatements, s => Assert.True(s.Contains("= @identifier") || s.Contains("= @id")));
            Assert.All(SqlTenantDataEraser.PlatformStatements, s => Assert.DoesNotContain("LIKE", s, StringComparison.OrdinalIgnoreCase));
        }

        // ---- Taking a copy first

        [Fact]
        public async Task An_export_holds_what_the_owner_made_with_a_link_to_each_upload()
        {
            var photoId = Guid.NewGuid();
            host.Context.Photos.Add(new Photo
            {
                PhotoId = photoId, Category = "weddings", SortOrder = 1, OriginalFileName = "sam-and-alex.jpg", OriginalBlobName = $"test/{photoId}/original.jpg",
                OriginalFileSize = 1234, Width = 3000, Height = 2000, Version = 1, VariantWidths = [400], Status = PhotoStatus.Ready, AltText = "On the dock",
            });
            host.Context.Placements.Add(new Placement { Key = "home-hero", PhotoId = photoId });
            host.Context.Stories.Add(new Story { StoryId = Guid.NewGuid(), Slug = "sam-and-alex", Category = "weddings", Title = "Sam & Alex" });
            var content = new SiteContentDocument { Text = new Dictionary<string, string> { ["home.heading"] = "Hello" } };
            host.Context.SiteContentDrafts.Add(new SiteContentDraft { Json = content.ToJson(), TemplateKey = "classic", TemplateVersion = 1 });
            host.Context.SiteContentVersions.Add(new SiteContentVersion { Version = 1, Json = content.ToJson(), TemplateKey = "classic", TemplateVersion = 1 });
            await host.Context.SaveChangesAsync();

            var storage = new Mock<IPhotoStorage>();
            storage.Setup(s => s.CreateOriginalDownloadUriAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string blob, TimeSpan _, CancellationToken _) => new Uri($"https://storage.example.com/originals/{blob}?sig=signed"));
            var accessor = new Mock<IMultiTenantContextAccessor<ElysianTenantInfo>>();
            accessor.Setup(a => a.MultiTenantContext).Returns(() => new MultiTenantContext<ElysianTenantInfo> { TenantInfo = tenant });

            var export = await new ExportSiteQueryHandler(host.Context, accessor.Object, TimeProvider.System, storage.Object).Handle(new ExportSiteQuery(), default);

            Assert.Equal(("test", "classic", 1), (export.Identifier, export.Template, export.TemplateVersion));
            Assert.Equal("Hello", export.Draft!.Text["home.heading"]);
            Assert.Equal(1, Assert.Single(export.Published).Version);
            Assert.Equal("Sam & Alex", Assert.Single(export.Stories).Title);
            Assert.Equal(photoId, export.Placements["home-hero"]);

            var photo = Assert.Single(export.Photos);
            Assert.Equal(("sam-and-alex.jpg", "On the dock", 1234L), (photo.FileName, photo.AltText, photo.FileSize));
            Assert.Equal($"https://storage.example.com/originals/test/{photoId}/original.jpg?sig=signed", photo.OriginalUrl);
            Assert.Equal(export.ExportedAt.Add(ExportSiteQueryHandler.LinkLifetime), export.LinksExpireAt);
            storage.Verify(s => s.CreateOriginalDownloadUriAsync(It.IsAny<string>(), ExportSiteQueryHandler.LinkLifetime, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task An_export_of_a_site_with_nothing_in_it_is_still_an_export()
        {
            var accessor = new Mock<IMultiTenantContextAccessor<ElysianTenantInfo>>();
            accessor.Setup(a => a.MultiTenantContext).Returns(() => new MultiTenantContext<ElysianTenantInfo> { TenantInfo = tenant });

            var export = await new ExportSiteQueryHandler(host.Context, accessor.Object, TimeProvider.System).Handle(new ExportSiteQuery(), default);

            Assert.Null(export.Draft);
            Assert.Null(export.LinksExpireAt);
            Assert.Empty(export.Photos);
            Assert.Empty(export.Published);
        }
    }
}
