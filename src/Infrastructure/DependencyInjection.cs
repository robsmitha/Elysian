using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using CapitolSharp.Congress;
using Elysian.Application.Features.Booking;
using Elysian.Application.Features.UserManagement;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Identity;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Resend;

namespace Elysian.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddElysianFeatures<TStrategy>(this IServiceCollection services, IConfiguration configuration)
            where TStrategy : IMultiTenantStrategy
        {
            services.AddSingleton<IClaimsPrincipalAccessor, ClaimsPrincipalAccessor>();
            services.AddScoped<IIdentityService, IdentityService>();

            services.AddDbContext<ElysianContext>((serviceProvider, options) =>
            {
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
                options.AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>());
            });

            services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();

            services.AddDbContext<TenantContext>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            services.AddMultiTenant<ElysianTenantInfo>()
                .WithEFCoreStore<TenantContext, ElysianTenantInfo>()
                .WithStrategy<TStrategy>(ServiceLifetime.Singleton, ["___tenant___"]);

            services.AddSingleton(TimeProvider.System);

            return services;
        }

        public static IServiceCollection AddContentManagementFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ContentManagementSettings>(configuration.GetSection(nameof(ContentManagementSettings)))
                .AddHttpClient<IWordPressService, WordPressService>((serviceProvider, httpClient) =>
            {
                var multiTenantContextAccessor = serviceProvider.GetRequiredService<IMultiTenantContextAccessor<ElysianTenantInfo>>();
                var options = serviceProvider.GetRequiredService<IOptions<ContentManagementSettings>>();
                
                httpClient.BaseAddress = string.IsNullOrEmpty(multiTenantContextAccessor.MultiTenantContext.TenantInfo?.CmsUrl)
                    ? options.Value.CmsUri
                    : new Uri(multiTenantContextAccessor.MultiTenantContext.TenantInfo.CmsUrl);
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
            services.AddTransient<IIncomeService, IncomeService>();
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

        /// <summary>
        /// Photo upload/processing/portfolio features. Requires <see cref="AddAzureStorageFeatures"/>.
        /// </summary>
        public static IServiceCollection AddPhotoFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            return services.Configure<PhotoStorageSettings>(configuration.GetSection(nameof(PhotoStorageSettings)))
                .AddScoped<IPhotoStorage, AzurePhotoStorage>()
                .AddSingleton<IPhotoProcessor, ImageSharpPhotoProcessor>();
        }

        /// <summary>
        /// Public contact form: Turnstile verification and delivery through Resend.
        /// Reads the "Resend" (ApiKey, ToEmailAddress, FromEmailAddress) and "Turnstile" (SecretKey) sections.
        /// Rate limiting is left to the host, since it depends on how the host sees client IPs.
        /// </summary>
        public static IServiceCollection AddContactFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<TurnstileSettings>(configuration.GetSection(TurnstileSettings.SectionName));

            AddResendEmail(services, configuration);
            services.AddHttpClient<IHumanVerificationService, TurnstileVerificationService>();

            return services;
        }

        /// <summary>
        /// Admin user management through Microsoft Entra ID: B2B guest invitations, app role assignments on the
        /// enterprise app, and invitation emails through Resend. Reads GRAPH_TENANT_ID, GRAPH_CLIENT_ID,
        /// GRAPH_CLIENT_SECRET, ENTERPRISE_APP_SP_ID, APP_ROLE_ID and APP_URL, and the "Resend" section. The Graph app
        /// needs the User.ReadWrite.All and AppRoleAssignment.ReadWrite.All application permissions.
        /// </summary>
        public static IServiceCollection AddUserManagementFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<EntraSettings>(settings => EntraSettings.Bind(settings, configuration));
            AddResendEmail(services, configuration);

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton(new GraphRetryOptions());

            // One credential for the app's lifetime so its token cache is shared
            services.AddSingleton<TokenCredential>(serviceProvider =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<EntraSettings>>().Value;
                var missing = settings.MissingSettings().ToList();
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException($"User management is missing settings: {string.Join(", ", missing)}.");
                }
                return new ClientSecretCredential(settings.TenantId, settings.ClientId, settings.ClientSecret);
            });

            // Invitation responses carry redeem URLs, so drop the factory's loggers
            services.AddHttpClient<IGraphApiClient, GraphApiClient>(httpClient =>
            {
                httpClient.BaseAddress = new Uri(GraphApiClient.BaseAddress);
            }).RemoveAllLoggers();

            services.AddScoped<IEntraDirectoryService, EntraDirectoryService>();
            services.AddScoped<UserManagementService>();

            return services;
        }

        /// <summary>
        /// Registers Resend and <see cref="IEmailService"/> once, for whichever features need email
        /// </summary>
        private static void AddResendEmail(IServiceCollection services, IConfiguration configuration)
        {
            if (services.Any(s => s.ServiceType == typeof(IResend)))
            {
                return;
            }

            services.Configure<ResendSettings>(configuration.GetSection(ResendSettings.SectionName));
            services.AddResend(options => options.ApiToken = configuration[$"{ResendSettings.SectionName}:{nameof(ResendSettings.ApiKey)}"]!);
            services.TryAddTransient<IEmailService, ResendEmailService>();
        }

        /// <summary>
        /// Instagram feed: per-tenant tokens in the OAuthToken table (connected with ConnectInstagramCommand) and a
        /// mirror of recent posts with thumbnails in the public photos container. Requires <see cref="AddAzureStorageFeatures"/>
        /// and <see cref="AddPhotoFeatures"/>. Hosts schedule <c>SyncInstagramPostsCommand</c> and
        /// <c>RefreshInstagramTokenCommand</c> themselves (e.g. timers), with the tenant set.
        /// </summary>
        public static IServiceCollection AddInstagramFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<InstagramSettings>(configuration.GetSection(InstagramSettings.SectionName));

            services.AddMemoryCache();
            services.TryAddSingleton(TimeProvider.System);

            services.AddScoped<IInstagramTokenStore, DbInstagramTokenStore>();

            // The token rides in the query string, so drop the factory's loggers, which write full request URIs.
            // The timeout also covers downloading full-size media for thumbnails.
            services.AddHttpClient<IInstagramService, InstagramService>(httpClient =>
            {
                httpClient.BaseAddress = new Uri("https://graph.instagram.com/");
                httpClient.Timeout = TimeSpan.FromSeconds(60);
            }).RemoveAllLoggers();

            return services;
        }

        /// <summary>
        /// Online booking against the tenant's Google Calendar: session products, availability, bookings and the
        /// connection's health check. Reads the "Booking" and "GoogleCalendar" sections; the refresh token is per tenant
        /// in the OAuthToken table (connected with ConnectGoogleCalendarCommand). Requires <see cref="AddContactFeatures"/>
        /// (emails and human verification) and <see cref="AddPhotoFeatures"/> (cover photos). Hosts schedule
        /// <c>CheckGoogleCalendarHealthCommand</c> themselves (e.g. a daily timer), with the tenant set, and rate limit bookings.
        /// </summary>
        public static IServiceCollection AddBookingFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<BookingSettings>(configuration.GetSection(BookingSettings.SectionName));
            services.Configure<GoogleCalendarSettings>(configuration.GetSection(GoogleCalendarSettings.SectionName));

            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(serviceProvider =>
                BookingRules.FromSettings(serviceProvider.GetRequiredService<IOptions<BookingSettings>>().Value));

            services.AddScoped<IGoogleTokenStore, DbGoogleTokenStore>();
            services.AddSingleton<IGoogleOAuthClient, GoogleOAuthClient>();
            services.AddScoped<IGoogleAccessTokenProvider, GoogleAccessTokenProvider>();
            services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
            services.AddScoped<BookingAvailabilityService>();

            return services;
        }
    }
}
