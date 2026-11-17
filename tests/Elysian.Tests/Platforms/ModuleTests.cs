using Elysian.Application.Behaviors;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Booking.Queries;
using Elysian.Application.Features.Contact.Commands;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.Platforms
{
    public class ModuleTests
    {
        private static readonly Type[] RequestTypes = typeof(RequestModules).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && t.GetInterfaces().Any(i => i == typeof(IBaseRequest)))
            .ToArray();

        [Fact]
        public void Every_feature_folder_is_assigned_a_module_or_marked_core()
        {
            var features = RequestTypes.Select(RequestModules.FeatureOf).Where(f => f != null).Distinct().ToList();

            Assert.NotEmpty(features);
            var unlisted = features.Where(f => !RequestModules.FeatureModules.ContainsKey(f!)).ToList();
            Assert.True(unlisted.Count == 0, $"Add these features to RequestModules.FeatureModules: {string.Join(", ", unlisted)}");
        }

        [Fact]
        public void Every_request_lives_in_a_feature_folder()
        {
            var outside = RequestTypes.Where(t => RequestModules.FeatureOf(t) == null).Select(t => t.FullName).ToList();

            Assert.True(outside.Count == 0, $"Requests outside Features can't be gated by module: {string.Join(", ", outside)}");
        }

        [Fact]
        public void Mapped_modules_are_known_modules()
        {
            var unknown = RequestModules.FeatureModules.Values.Where(m => m != null && !ModuleNames.All.Contains(m)).ToList();

            Assert.Empty(unknown);
        }

        [Fact]
        public void A_request_takes_the_module_of_its_feature_folder()
        {
            Assert.Equal(ModuleNames.Booking, RequestModules.For(typeof(GetBookingAvailabilityQuery)));
            Assert.Null(RequestModules.For(typeof(SendContactMessageCommand)));
        }

        [Fact]
        public void The_attribute_overrides_the_feature_folder()
        {
            Assert.Equal(ModuleNames.Finance, RequestModules.For(typeof(MarkedRequest)));
            Assert.Null(RequestModules.For(typeof(OpenRequest)));
        }

        [Fact]
        public void Photography_includes_photos_and_sells_booking_and_instagram()
        {
            var platform = PlatformCatalog.Find("Photography")!;

            Assert.Equal([ModuleNames.Photos], platform.IncludedModules);
            Assert.True(platform.Offers(ModuleNames.Booking));
            Assert.True(platform.Offers(ModuleNames.Instagram));
            Assert.False(platform.Offers(ModuleNames.Finance));
            Assert.Null(PlatformCatalog.Find("unknown"));
        }

        [Fact]
        public async Task Nothing_is_refused_while_enforcement_is_off()
        {
            using var host = new TestHost();
            var service = Service(host, enforce: false);

            Assert.True(await service.IsEnabledAsync(ModuleNames.Booking));
            await Behavior(service).Handle(new GatedRequest(), () => Task.FromResult(Unit.Value), CancellationToken.None);
        }

        [Fact]
        public async Task A_module_the_tenant_lacks_is_refused_before_the_handler_runs()
        {
            using var host = new TestHost();
            var service = Service(host, enforce: true);
            var handled = false;

            var exception = await Assert.ThrowsAsync<ModuleNotEnabledException>(() =>
                Behavior(service).Handle(new GatedRequest(), () => { handled = true; return Task.FromResult(Unit.Value); }, CancellationToken.None));

            Assert.Equal(ModuleNames.Finance, exception.Module);
            Assert.IsAssignableFrom<ForbiddenAccessException>(exception);
            Assert.False(handled);
        }

        [Theory]
        [InlineData(TenantModuleStatus.Active, true)]
        [InlineData(TenantModuleStatus.Trial, true)]
        [InlineData(TenantModuleStatus.Suspended, false)]
        public async Task Only_active_and_trial_modules_are_usable(TenantModuleStatus status, bool expected)
        {
            using var host = new TestHost();
            host.Context.TenantModules.Add(new TenantModule { ModuleKey = ModuleNames.Finance, Status = status });
            await host.Context.SaveChangesAsync();

            Assert.Equal(expected, await Service(host, enforce: true).IsEnabledAsync(ModuleNames.Finance));
        }

        [Fact]
        public async Task Requests_outside_any_module_always_pass()
        {
            using var host = new TestHost();
            var handled = false;

            await new ModuleBehavior<OpenRequest, Unit>(Service(host, enforce: true))
                .Handle(new OpenRequest(), () => { handled = true; return Task.FromResult(Unit.Value); }, CancellationToken.None);

            Assert.True(handled);
        }

        [Fact]
        public async Task Modules_are_cached_per_tenant_until_invalidated()
        {
            using var host = new TestHost();
            var service = Service(host, enforce: true);
            Assert.False(await service.IsEnabledAsync(ModuleNames.Finance));

            host.Context.TenantModules.Add(new TenantModule { ModuleKey = ModuleNames.Finance, Status = TenantModuleStatus.Active });
            await host.Context.SaveChangesAsync();

            Assert.False(await service.IsEnabledAsync(ModuleNames.Finance));
            service.Invalidate();
            Assert.True(await service.IsEnabledAsync(ModuleNames.Finance));
        }

        private static TenantModuleService Service(TestHost host, bool enforce) => new(
            host.Context,
            host.TenantAccessor,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new ModuleSettings { Enforce = enforce }));

        private static ModuleBehavior<GatedRequest, Unit> Behavior(TenantModuleService service) => new(service);

        [RequiresModule(ModuleNames.Finance)]
        private record MarkedRequest : IRequest<Unit>;

        [RequiresModule(ModuleNames.Finance)]
        private record GatedRequest : IRequest<Unit>;

        [RequiresModule(null)]
        private record OpenRequest : IRequest<Unit>;
    }
}
