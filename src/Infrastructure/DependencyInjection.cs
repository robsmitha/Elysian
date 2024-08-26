using Azure.Storage.Blobs;
using CapitolSharp.Congress;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Identity;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddElysianFeatures<TStrategy>(this IServiceCollection services, string connectionString, string tenantHeader = "___tenant___")
            where TStrategy : IMultiTenantStrategy
        {
            services.AddSingleton<IClaimsPrincipalAccessor, ClaimsPrincipalAccessor>();

            services.AddDbContext<ElysianContext>(options => options.UseSqlServer(connectionString));

            services.AddDbContext<TenantContext>(options => options.UseSqlServer(connectionString));

            services.AddMultiTenant<ElysianTenantInfo>()
                .WithEFCoreStore<TenantContext, ElysianTenantInfo>()
                .WithStrategy<TStrategy>(ServiceLifetime.Singleton, [tenantHeader]);

            return services;
        }

        public static IServiceCollection AddContentManagementFeatures(this IServiceCollection services)
        {
            services.AddHttpClient<IWordPressService, WordPressService>((serviceProvider, httpClient) =>
            {
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                httpClient.BaseAddress = configuration.GetValue<Uri>("WordPressCmsUri");
            });
            return services;
        }

        public static IServiceCollection AddCodeFeatures(this IServiceCollection services)
        {
            services.AddHttpClient("GitHubApi", (serviceProvider, httpClient) =>
            {
                httpClient.BaseAddress = new Uri("https://api.github.com");

                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var accessToken = configuration.GetValue<string>("DefaultAccessTokens:GitHub");
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
                httpClient.DefaultRequestHeaders.Add("User-Agent", "robsmitha.com");
                httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            });
            
            services.AddHttpClient("GitHubOAuth", (httpClient) =>
            {
                httpClient.BaseAddress = new Uri("https://github.com");
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            });
            
            services.AddTransient<IGitHubService, GitHubService>();

            return services;
        }

        public static IServiceCollection AddCongressFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<CongressApiSettings>(config =>
            {
                config.ApiKey = configuration.GetValue<string>("DefaultAccessTokens:CongressGov");
            });

            services.AddTransient<CapitolSharpCongress>(serviceProvider =>
            {
                var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                var congressApiSettings = serviceProvider.GetRequiredService<IOptions<CongressApiSettings>>();
                return new(httpClientFactory.CreateClient(), congressApiSettings.Value);
            });

            return services;
        }

        public static IServiceCollection AddFinancialFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<PlaidSettings>(configuration.GetSection(nameof(PlaidSettings)));

            services.AddTransient<ICategoryService, CategoryService>();
            services.AddTransient<IAccessTokenService, AccessTokenService>();
            services.AddTransient<IBudgetService, BudgetService>();
            services.AddTransient<IFinancialService, PlaidService>();
            services.AddHttpClient("PlaidClient", (serviceProvider, httpClient) =>
            {
                var plaidSettings = serviceProvider.GetRequiredService<IOptions<PlaidSettings>>();
                httpClient.BaseAddress = new Uri(plaidSettings.Value.BaseUrl);
            });
            return services;
        }

        public static IServiceCollection AddAzureStorageFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            return services.Configure<AzureStorageSettings>(configuration.GetSection("AzureStorage"))
                .AddSingleton(serviceProvider =>
                {
                    var azureStorageSettings = serviceProvider.GetService<IOptions<AzureStorageSettings>>();
                    return new BlobServiceClient(azureStorageSettings.Value.ConnectionString);
                })
                .AddScoped<IAzureStorageClient, AzureStorageClient>();
        }
    }
}
