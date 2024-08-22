using Azure.Storage.Blobs;
using CapitolSharp.Congress;
using Elysian.Application.Features.MultiTenant;
using Elysian.Application.Interfaces;
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthenticationFeatures();

            services.AddContentManagementFeatures();

            services.AddCodeFeatures();

            services.AddCongressFeatures(configuration);

            services.AddFinancialFeatures(configuration);

            services.AddAzureStorageFeatures(configuration);

            services.AddMultiTenantFeatures(configuration);

            return services;
        }

        private static IServiceCollection AddMultiTenantFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<ElysianContext>(options 
                => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            services.AddDbContext<TenantContext>(options
                => options.UseSqlServer(configuration.GetConnectionString("TenantConnection")));

            services.AddMultiTenant<ElysianTenantInfo>()
                .WithEFCoreStore<TenantContext, ElysianTenantInfo>()
                .WithHeaderStrategy();

            return services;
        }

        private static IServiceCollection AddAuthenticationFeatures(this IServiceCollection services)
        {
            return services.AddSingleton<IClaimsPrincipalAccessor, ClaimsPrincipalAccessor>();
        }

        private static IServiceCollection AddContentManagementFeatures(this IServiceCollection services)
        {
            services.AddHttpClient<IWordPressService, WordPressService>((serviceProvider, httpClient) =>
            {
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                httpClient.BaseAddress = configuration.GetValue<Uri>("WordPressCmsUri");
            });
            return services;
        }

        private static IServiceCollection AddCodeFeatures(this IServiceCollection services)
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

        private static IServiceCollection AddCongressFeatures(this IServiceCollection services, IConfiguration configuration)
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

        private static IServiceCollection AddFinancialFeatures(this IServiceCollection services, IConfiguration configuration)
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

        private static IServiceCollection AddAzureStorageFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            return services.Configure<AzureStorageSettings>(configuration.GetSection("AzureStorage:Key"))
                .AddSingleton(sp =>
                {
                    var config = sp.GetService<IConfiguration>();
                    var azureStorageConnectionString = configuration.GetConnectionString("AzureStorageConnectionString");
                    return new BlobServiceClient(azureStorageConnectionString);
                });
        }
    }
}
