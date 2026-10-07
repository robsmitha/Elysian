using Azure.Core;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elysian.Tests.UserManagement
{
    public class UserManagementRegistrationTests
    {
        private static ServiceProvider Build(Dictionary<string, string?> settings)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddUserManagementFeatures(configuration);
            services.AddContactFeatures(configuration);
            return services.BuildServiceProvider();
        }

        [Fact]
        public void Resolves_the_graph_client_from_flat_settings()
        {
            using var provider = Build(new()
            {
                ["GRAPH_TENANT_ID"] = Guid.NewGuid().ToString(),
                ["GRAPH_CLIENT_ID"] = Guid.NewGuid().ToString(),
                ["GRAPH_CLIENT_SECRET"] = "not-a-real-secret",
                ["ENTERPRISE_APP_SP_ID"] = Guid.NewGuid().ToString(),
                ["APP_URL"] = "https://app.example.com",
                ["Resend:ApiKey"] = "re_test",
            });

            Assert.NotNull(provider.GetRequiredService<IGraphApiClient>());
            Assert.NotNull(provider.GetRequiredService<IEmailService>());

            // Registering email from two features must not register it twice
            Assert.Single(provider.GetServices<IEmailService>());
        }

        [Fact]
        public void Missing_settings_are_named_without_echoing_values()
        {
            using var provider = Build(new() { ["GRAPH_CLIENT_SECRET"] = "super-secret-value" });

            var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<TokenCredential>());

            Assert.Contains("GRAPH_TENANT_ID", ex.Message);
            Assert.Contains("ENTERPRISE_APP_SP_ID", ex.Message);
            Assert.DoesNotContain("GRAPH_CLIENT_SECRET", ex.Message);
            Assert.DoesNotContain("super-secret-value", ex.Message);
        }
    }
}
