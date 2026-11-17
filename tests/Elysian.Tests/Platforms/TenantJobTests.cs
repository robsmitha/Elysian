using Elysian.Application.Exceptions;
using Elysian.Application.Features.GoogleCalendar.Commands;
using Elysian.Application.Features.Instagram.Commands;
using Elysian.Application.Features.Platform;
using Elysian.Application.Features.Platform.Commands;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Elysian.Tests.Platforms
{
    public sealed class TenantJobTests : IDisposable
    {
        private static readonly ElysianTenantInfo Aspen = new() { Id = "t-aspen", Identifier = "aspensmitha", Status = TenantStatusNames.Active };
        private static readonly ElysianTenantInfo Friend = new() { Id = "t-friend", Identifier = "friend", Status = TenantStatusNames.Trial };
        private static readonly ElysianTenantInfo Paused = new() { Id = "t-paused", Identifier = "paused", Status = TenantStatusNames.Suspended };
        private static readonly ElysianTenantInfo Rob = new() { Id = "t-rob", Identifier = "robsmitha" };

        private readonly ServiceProvider tenants;
        private readonly PlatformContext platform;
        private readonly RecordingQueue queue = new();

        public TenantJobTests()
        {
            var services = new ServiceCollection();
            services.AddMultiTenant<ElysianTenantInfo>().WithInMemoryStore(options =>
            {
                foreach (var tenant in new[] { Aspen, Friend, Paused, Rob })
                {
                    options.Tenants.Add(tenant);
                }
            });
            tenants = services.BuildServiceProvider();
            platform = new PlatformContext(new DbContextOptionsBuilder<PlatformContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            AddModule(Aspen, ModuleNames.Instagram, TenantModuleStatus.Active);
            AddModule(Aspen, ModuleNames.Booking, TenantModuleStatus.Active);
            AddModule(Friend, ModuleNames.Instagram, TenantModuleStatus.Trial);
            AddModule(Paused, ModuleNames.Instagram, TenantModuleStatus.Active);
            AddModule(Rob, ModuleNames.Instagram, TenantModuleStatus.Suspended);
            AddModule(Rob, ModuleNames.Finance, TenantModuleStatus.Active);
            platform.SaveChanges();
        }

        public void Dispose()
        {
            platform.Dispose();
            tenants.Dispose();
        }

        [Fact]
        public void Every_job_belongs_to_a_known_module_and_creates_a_request()
        {
            Assert.NotEmpty(TenantJobCatalog.All);
            Assert.Equal(TenantJobCatalog.All.Count, TenantJobCatalog.All.Select(j => j.Key).Distinct().Count());
            foreach (var job in TenantJobCatalog.All)
            {
                Assert.True(job.Module == null || ModuleNames.All.Contains(job.Module), job.Key);
                Assert.NotNull(job.CreateRequest());
            }
        }

        [Fact]
        public void The_message_names_its_tenant_where_the_queue_trigger_looks_for_it()
        {
            var body = new TenantJobMessage("aspensmitha", TenantJobCatalog.InstagramSync).Serialize();

            Assert.Contains("\"tenant\":\"aspensmitha\"", body);
            Assert.Equal(new TenantJobMessage("aspensmitha", TenantJobCatalog.InstagramSync), TenantJobMessage.Deserialize(body));
            Assert.Throws<InvalidOperationException>(() => TenantJobMessage.Deserialize("""{"job":"instagram-sync"}"""));
        }

        [Fact]
        public async Task A_job_is_queued_once_for_each_tenant_that_has_the_module_and_isnt_suspended()
        {
            var result = await Enqueue(TenantJobCatalog.InstagramSync);

            Assert.Equal(new EnqueueTenantJobsResult(2, 0), result);
            Assert.Equal(["aspensmitha", "friend"], queue.Messages.Select(m => m.Tenant).Order());
            Assert.All(queue.Messages, m => Assert.Equal(TenantJobCatalog.InstagramSync, m.Job));
        }

        [Fact]
        public async Task A_different_job_goes_to_a_different_set_of_tenants()
        {
            await Enqueue(TenantJobCatalog.CalendarHealth);

            Assert.Equal("aspensmitha", Assert.Single(queue.Messages).Tenant);
        }

        [Fact]
        public async Task One_tenants_message_failing_doesnt_cost_the_others_their_run()
        {
            queue.FailFor = "aspensmitha";

            var result = await Enqueue(TenantJobCatalog.InstagramSync);

            Assert.Equal(new EnqueueTenantJobsResult(1, 1), result);
            Assert.Equal("friend", Assert.Single(queue.Messages).Tenant);
        }

        [Fact]
        public async Task An_unknown_job_is_refused()
        {
            await Assert.ThrowsAsync<NotFoundException>(() => Enqueue("no-such-job"));
            await Assert.ThrowsAsync<NotFoundException>(() => Run("no-such-job", new Mock<ISender>().Object));
        }

        [Fact]
        public async Task Running_a_job_sends_that_jobs_own_request()
        {
            var sender = new Mock<ISender>();
            sender.Setup(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>())).ReturnsAsync("ok");

            Assert.Equal("ok", await Run(TenantJobCatalog.InstagramSync, sender.Object));
            await Run(TenantJobCatalog.CalendarHealth, sender.Object);

            sender.Verify(s => s.Send(It.Is<object>(r => r is SyncInstagramPostsCommand), It.IsAny<CancellationToken>()), Times.Once);
            sender.Verify(s => s.Send(It.Is<object>(r => r is CheckGoogleCalendarHealthCommand), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_job_whose_module_was_switched_off_after_it_was_queued_is_skipped_not_retried()
        {
            var sender = new Mock<ISender>();
            sender.Setup(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ModuleNotEnabledException(ModuleNames.Instagram));

            Assert.Equal(RunTenantJobCommandHandler.Skipped, await Run(TenantJobCatalog.InstagramSync, sender.Object));
        }

        private Task<EnqueueTenantJobsResult> Enqueue(string job) =>
            new EnqueueTenantJobsCommandHandler(platform, tenants.GetRequiredService<IMultiTenantStore<ElysianTenantInfo>>(), queue,
                NullLogger<EnqueueTenantJobsCommandHandler>.Instance).Handle(new EnqueueTenantJobsCommand(job), CancellationToken.None);

        private static Task<string> Run(string job, ISender sender) =>
            new RunTenantJobCommandHandler(sender, NullLogger<RunTenantJobCommandHandler>.Instance).Handle(new RunTenantJobCommand(job), CancellationToken.None);

        private void AddModule(ElysianTenantInfo tenant, string module, TenantModuleStatus status) =>
            platform.TenantModules.Add(new TenantModule
            {
                TenantId = tenant.Id!, ModuleKey = module, Status = status, CreatedByUserId = "test", ModifiedByUserId = "test"
            });

        private sealed class RecordingQueue : ITenantJobQueue
        {
            public List<TenantJobMessage> Messages { get; } = [];
            public string? FailFor { get; set; }

            public Task EnqueueAsync(TenantJobMessage message, CancellationToken cancellationToken = default)
            {
                if (message.Tenant == FailFor)
                {
                    throw new InvalidOperationException("Queue unavailable");
                }
                Messages.Add(message);
                return Task.CompletedTask;
            }
        }
    }
}
