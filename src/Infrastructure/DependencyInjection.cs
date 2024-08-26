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
        public static IServiceCollection AddElysianFeatures<TStrategy>(this IServiceCollection services, IConfiguration configuration)
            where TStrategy : IMultiTenantStrategy
        {
            services.AddSingleton<IClaimsPrincipalAccessor, ClaimsPrincipalAccessor>();

            services.AddDbContext<ElysianContext>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            services.AddDbContext<TenantContext>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            services.AddMultiTenant<ElysianTenantInfo>()
                .WithEFCoreStore<TenantContext, ElysianTenantInfo>()
                .WithStrategy<TStrategy>(ServiceLifetime.Singleton, ["___tenant___"]);

            return services;
        }

        public static IServiceCollection AddContentManagementFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ContentManagementSettings>(configuration.GetSection(nameof(ContentManagementSettings)))
                .AddHttpClient<IWordPressService, WordPressService>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<ContentManagementSettings>>();
                httpClient.BaseAddress = options.Value.CmsUri;
            });
            return services;
        }

        public static IServiceCollection AddCodeFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<GitHubSettings>(configuration.GetSection(nameof(GitHubSettings)))
                .AddHttpClient("GitHubApi", (serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<GitHubSettings>>();
                httpClient.BaseAddress = new Uri("https://api.github.com");
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.Value.DefaultAccessToken}");
                httpClient.DefaultRequestHeaders.Add("User-Agent", options.Value.UserAgent);
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
            services.Configure<CongressApiSettings>(configuration.GetSection(nameof(CongressApiSettings)));

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
            return services.Configure<AzureStorageSettings>(configuration.GetSection(nameof(AzureStorageSettings)))
                .AddSingleton(serviceProvider =>
                {
                    var azureStorageSettings = serviceProvider.GetService<IOptions<AzureStorageSettings>>();
                    return new BlobServiceClient(azureStorageSettings.Value.ConnectionString);
                })
                .AddScoped<IAzureStorageClient, AzureStorageClient>();
        }
    }
}
