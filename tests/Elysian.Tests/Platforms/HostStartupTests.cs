using Elysian.Application;
using Elysian.Application.Behaviors;
using Elysian.Application.Features.Placements;
using Elysian.Domain.Data;
using Elysian.Infrastructure;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elysian.Tests.Platforms
{
    /// <summary>
    /// A shared host registers every feature whether or not it's configured, because whether a tenant has a
    /// feature is decided per request. A missing setting may fail the request that needs it, never the host.
    /// </summary>
    public class HostStartupTests
    {
        /// <summary>
        /// Features whose handlers must be constructible with nothing configured. Add-ons a tenant may never
        /// buy belong here; a handler that can't even be built would take the whole request pipeline down.
        /// </summary>
        private static readonly string[] MustResolveUnconfigured = ["Booking", "GoogleCalendar", "Instagram", "Contact", "Stories", "SiteContent"];

        private const string CatalogJson = """
            {
              "recipeVersion": 1,
              "siteDefaultKey": "site-default",
              "slots": [ { "key": "home-hero-1", "name": "home-hero-strip-photo1", "ratio": 0.75, "widths": [480], "alt": "Hero" } ]
            }
            """;

        private static (ServiceCollection Services, ServiceProvider Provider) BuildHost()
        {
            // The database and the storage account are what every feature needs. Nothing connects to either here.
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=(local);Database=none;Encrypt=false",
                ["AzureStorageSettings:ConnectionString"] = "UseDevelopmentStorage=true",
            }).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddElysianFeatures<NoTenantStrategy>(configuration);
            services.AddContentManagementFeatures(configuration);
            services.AddCodeFeatures(configuration);
            services.AddCongressFeatures(configuration);
            services.AddFinancialFeatures(configuration);
            services.AddAssistantFeatures(configuration);
            services.AddAzureStorageFeatures(configuration);
            services.AddPhotoFeatures(configuration);
            services.AddPlacementFeatures(configuration, PlacementCatalog.FromJson(CatalogJson));
            services.AddContactFeatures(configuration);
            services.AddUserManagementFeatures(configuration);
            services.AddInstagramFeatures(configuration);
            services.AddBookingFeatures(configuration);
            services.AddSiteContentFeatures(configuration);
            services.AddApplication();

            return (services, services.BuildServiceProvider());
        }

        [Fact]
        public void Every_feature_registers_with_only_a_database_and_storage_configured()
        {
            var (_, provider) = BuildHost();
            provider.Dispose();
        }

        [Fact]
        public void Add_on_handlers_can_be_built_with_only_a_database_and_storage_configured()
        {
            var (services, provider) = BuildHost();
            using var _ = provider;
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext =
                new MultiTenantContext<ElysianTenantInfo> { TenantInfo = TestHost.Tenant };

            var handlers = services
                .Where(d => d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericTypeDefinition() is var definition
                    && (definition == typeof(IRequestHandler<,>) || definition == typeof(IRequestHandler<>)))
                .Where(d => d.ImplementationType != null
                    && MustResolveUnconfigured.Contains(RequestModules.FeatureOf(d.ImplementationType)))
                .ToList();

            Assert.NotEmpty(handlers);
            var failures = new List<string>();
            foreach (var handler in handlers)
            {
                try
                {
                    scope.ServiceProvider.GetRequiredService(handler.ServiceType);
                }
                catch (Exception ex)
                {
                    failures.Add($"{handler.ImplementationType!.Name}: {ex.GetBaseException().Message}");
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }

        /// <summary>
        /// Some requests run before any tenant is known: a timer queuing jobs, someone redeeming an invite or
        /// listing their sites. Everything the pipeline builds for them must cope with there being no tenant.
        /// </summary>
        [Theory]
        [InlineData(typeof(Elysian.Application.Features.Platform.Commands.EnqueueTenantJobsCommand))]
        [InlineData(typeof(Elysian.Application.Features.Members.Commands.RedeemInviteCommand))]
        [InlineData(typeof(Elysian.Application.Features.Members.Queries.GetMyTenantsQuery))]
        public void Requests_that_run_outside_a_tenant_can_be_built_with_no_tenant_set(Type requestType)
        {
            var (_, provider) = BuildHost();
            using var _provider = provider;
            using var scope = provider.CreateScope();

            var responseType = requestType.GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)).GetGenericArguments()[0];

            var behaviors = scope.ServiceProvider.GetServices(typeof(IPipelineBehavior<,>).MakeGenericType(requestType, responseType)).ToList();
            var handler = scope.ServiceProvider.GetRequiredService(typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType));

            Assert.NotEmpty(behaviors);
            Assert.NotNull(handler);
        }

        private class NoTenantStrategy : IMultiTenantStrategy
        {
            public Task<string?> GetIdentifierAsync(object context) => Task.FromResult<string?>(null);
        }
    }
}
