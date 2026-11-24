using Elysian.Infrastructure.Assistant;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using CapitolSharp.Congress;
using Elysian.Application.Features.Booking;
using Elysian.Application.Features.Placements;
using Elysian.Application.Features.SiteContent;
using Elysian.Application.Features.Sites;
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
using Microsoft.Extensions.Logging;
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

            // Which modules each tenant has. Nothing is refused unless Modules:Enforce is on.
            services.Configure<ModuleSettings>(configuration.GetSection(ModuleSettings.SectionName));
            services.AddMemoryCache();
            services.AddScoped<ITenantModuleService, TenantModuleService>();

            // Settings and secrets that differ per tenant, falling back to this host's configuration
            services.Configure<TenantSettingsOptions>(configuration.GetSection(TenantSettingsOptions.SectionName));
            services.AddScoped(typeof(ITenantSettings<>), typeof(TenantSettingsReader<>));

            // What each site says about itself: categories, featured spots, page layouts
            services.TryAddScoped<SiteManifestReader>();
            services.Configure<TenantSecretOptions>(configuration.GetSection(TenantSecretOptions.SectionName));
            var tenantSecretVaultUri = configuration[$"{TenantSecretOptions.SectionName}:{nameof(TenantSecretOptions.VaultUri)}"];
            if (Uri.TryCreate(tenantSecretVaultUri, UriKind.Absolute, out var vaultUri))
            {
                services.AddSingleton(new SecretClient(vaultUri, new DefaultAzureCredential()));
                services.AddScoped<ITenantSecretStore, KeyVaultTenantSecretStore>();
            }
            else
            {
                services.AddScoped<ITenantSecretStore>(serviceProvider => new ConfigurationTenantSecretStore(configuration,
                    serviceProvider.GetRequiredService<IMultiTenantContextAccessor<ElysianTenantInfo>>()));
            }

            services.AddDbContext<ElysianContext>((serviceProvider, options) =>
            {
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
                options.AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>());
                // Declares the request's tenant to SQL Server for row-level security
                options.AddInterceptors(new TenantSessionContextInterceptor());
            });

            services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();

            services.AddDbContext<TenantContext>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            services.AddMultiTenant<ElysianTenantInfo>()
                .WithEFCoreStore<TenantContext, ElysianTenantInfo>()
                .WithStrategy<TStrategy>(ServiceLifetime.Singleton, ["___tenant___"]);

            services.AddSingleton(TimeProvider.System);

            // Platform data read before the request's tenant is known: who at the edge may act for which tenant
            services.AddDbContext<PlatformContext>(options => options
                .UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(new TenantSessionContextInterceptor(bypass: true)));
            // Removing a deleted site's rows, across its tables, on that connection
            services.AddScoped<ITenantDataEraser, SqlTenantDataEraser>();
            services.Configure<EdgeSettings>(configuration.GetSection(EdgeSettings.SectionName));
            services.AddScoped<IEdgeClientValidator, EdgeClientValidator>();

            // Scheduled work, queued once per tenant
            services.AddSingleton<ITenantJobQueue>(new AzureTenantJobQueue(configuration));

            // Who is signed in, and where their roles come from. Hosts serving one site keep the default.
            services.Configure<AuthSettings>(configuration.GetSection(AuthSettings.SectionName));
            // The domain a platform's new sites get their first address under
            services.Configure<PlatformDomainSettings>(configuration.GetSection(PlatformDomainSettings.SectionName));
            // How long a deleted site is kept before its photos and data are removed
            services.Configure<PlatformDeletionSettings>(configuration.GetSection(PlatformDeletionSettings.SectionName));
            services.AddSingleton<ITokenValidator>(serviceProvider => new JwtTokenValidator(
                serviceProvider.GetRequiredService<IOptions<AuthSettings>>(),
                serviceProvider.GetRequiredService<ILogger<JwtTokenValidator>>()));

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
            services.AddTransient(_ => new PlaidRetryHandler());
            services.AddHttpClient("PlaidClient", (serviceProvider, httpClient) =>
            {
                var plaidSettings = serviceProvider.GetRequiredService<IOptions<PlaidSettings>>();
                httpClient.BaseAddress = new Uri(plaidSettings.Value.BaseUrl);
            })
            // Temporary Plaid failures (rate limits, 5xx, bank outages) are retried with backoff
            .AddHttpMessageHandler<PlaidRetryHandler>();

            // Stored bank data: encrypted tokens, the sync from /transactions/sync, webhooks, and the pages' reads
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IAccessTokenProtector, AccessTokenProtector>();
            services.AddSingleton<IPlaidSyncQueue, AzurePlaidSyncQueue>();
            services.AddTransient<ITransactionSyncService, TransactionSyncService>();
            services.AddTransient<IConnectionService, ConnectionService>();
            services.AddTransient<ITransactionClassifier, TransactionClassifier>();
            services.AddTransient<ITransactionService, TransactionService>();
            services.AddTransient<IBudgetMonthService, BudgetMonthService>();
            services.AddTransient<IDebtService, DebtService>();
            services.AddTransient<IGoalService, GoalService>();
            services.AddTransient<PlaidWebhookVerifier>();
            services.AddTransient<IPlaidWebhookService, PlaidWebhookService>();
            return services;
        }

        /// <summary>The budget assistant (docs/assistant-plan.md). Needs AddFinancialFeatures. Assistant__Provider picks Claude or the offline fake.</summary>
        public static IServiceCollection AddAssistantFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<AssistantSettings>(configuration.GetSection("Assistant"));
            services.Configure<AnthropicSettings>(configuration.GetSection("Anthropic"));
            services.TryAddSingleton(TimeProvider.System);
            // Both kept for the life of the process; which one answers is read from the settings on each request
            services.AddSingleton<AnthropicAssistantModel>();
            services.AddSingleton<FakeAssistantModel>();
            services.AddTransient<IAssistantModel>(serviceProvider =>
                serviceProvider.GetRequiredService<IOptionsMonitor<AssistantSettings>>().CurrentValue.Provider == "anthropic"
                    ? serviceProvider.GetRequiredService<AnthropicAssistantModel>()
                    : serviceProvider.GetRequiredService<FakeAssistantModel>());
            services.AddTransient<AssistantLimiter>();
            services.AddTransient<AssistantTools>();
            services.AddTransient<IAssistantService, AssistantService>();
            services.AddTransient<IAssistantActionService, AssistantActions>();
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
        /// Site content: what an owner changes within a shared template (colours, wording, links), edited as a
        /// draft and published to the edge. Pass the template schemas this host was built with. Editing works
        /// with nothing else configured; previews need StoryPreview:SigningKey, and publishing needs the edge
        /// store (the EdgeStore section).
        /// </summary>
        public static IServiceCollection AddSiteContentFeatures(this IServiceCollection services, IConfiguration configuration,
            SiteTemplateCatalog? templates = null)
        {
            services.AddSingleton(templates ?? new SiteTemplateCatalog());
            services.AddScoped<SiteContentReader>();
            services.AddScoped<SiteTemplateChanger>();

            services.Configure<StoryPreviewSettings>(configuration.GetSection(StoryPreviewSettings.SectionName));
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ISitePreviewTokens, HmacSitePreviewTokens>();

            // The edge store: published content and the host map, in a Cloudflare KV namespace. Without the
            // EdgeStore settings only publishing fails; everything else carries on.
            services.Configure<EdgeStoreSettings>(configuration.GetSection(EdgeStoreSettings.SectionName));
            services.AddHttpClient<IEdgeConfigStore, CloudflareKvEdgeStore>((serviceProvider, httpClient) =>
            {
                httpClient.BaseAddress = new Uri(serviceProvider.GetRequiredService<IOptions<EdgeStoreSettings>>().Value.ApiBaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(15);
            });
            services.TryAddScoped<EdgeSites>();
            services.TryAddScoped<ISiteContentEdge>(serviceProvider => serviceProvider.GetRequiredService<EdgeSites>());
            services.TryAddScoped<IEdgeHostMap>(serviceProvider => serviceProvider.GetRequiredService<EdgeSites>());
            return services;
        }

        /// <summary>
        /// Photo upload/processing/portfolio features. Requires <see cref="AddAzureStorageFeatures"/>.
        /// </summary>
        public static IServiceCollection AddPhotoFeatures(this IServiceCollection services, IConfiguration configuration)
        {
            // Story previews: links signed with StoryPreview:SigningKey. Without the key only previews fail.
            services.Configure<StoryPreviewSettings>(configuration.GetSection(StoryPreviewSettings.SectionName));
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IStoryPreviewTokens, HmacStoryPreviewTokens>();

            // Which images host a tenant's photos are served from (see TenantImageHost)
            services.Configure<PlatformImagesSettings>(configuration.GetSection(PlatformImagesSettings.SectionName));
            services.TryAddScoped<TenantImageHost>();

            return services.Configure<PhotoStorageSettings>(configuration.GetSection(nameof(PhotoStorageSettings)))
                .AddScoped<IPhotoStorage, AzurePhotoStorage>()
                .AddSingleton<IPhotoProcessor, ImageSharpPhotoProcessor>();
        }

        /// <summary>
        /// Reserved placement images: each slot gets fixed CDN URLs that are re-rendered, overwritten and purged when
        /// what it shows changes. A tenant's slots come from its site manifest. <paramref name="catalog"/> is for
        /// tenants without one: a host serving one site passes that site's catalog; a host serving many passes
        /// none, so a tenant without a manifest publishes nothing. Requires <see cref="AddPhotoFeatures"/>. Reads the
        /// "PlacementPublishing" and "Cloudflare" sections, and the PlacementsQueue connection the host's queue trigger
        /// uses (PlacementsQueue__queueServiceUri with managed identity, or a PlacementsQueue connection string).
        /// The host: enqueues on photo changes (<see cref="IPlacementPublishQueue"/>), sends PublishPlacementsCommand
        /// from a queue trigger on <see cref="PlacementQueue.QueueName"/>, and exposes RepublishPlacementsCommand and
        /// GetPlacementStatusQuery to admins.
        /// </summary>
        public static IServiceCollection AddPlacementFeatures(this IServiceCollection services, IConfiguration configuration,
            PlacementCatalog? catalog = null)
        {
            services.Configure<PlacementPublishingSettings>(configuration.GetSection(PlacementPublishingSettings.SectionName));
            services.Configure<CloudflareSettings>(configuration.GetSection(CloudflareSettings.SectionName));
            services.Configure<PlatformImagesSettings>(configuration.GetSection(PlatformImagesSettings.SectionName));
            services.TryAddScoped<TenantImageHost>();

            services.AddSingleton(new HostPlacementCatalog(catalog));
            services.AddMemoryCache();
            services.AddOptions<TenantSettingsOptions>();
            services.TryAddScoped<SiteManifestReader>();
            services.AddScoped<IPlacementCatalogSource, TenantPlacementCatalogSource>();
            services.AddSingleton(serviceProvider => new PlacementStorageClients(
                serviceProvider.GetRequiredService<IOptions<PlacementPublishingSettings>>().Value, configuration));

            services.AddScoped<PlacementResolver>();
            services.AddScoped<IPlacementStore, AzurePlacementStore>();
            services.AddScoped<IPlacementPublishQueue, AzurePlacementPublishQueue>();
            services.AddSingleton<IPlacementRenderer, ImageSharpPlacementRenderer>();
            services.AddHttpClient<ICdnPurger, CloudflareCdnPurger>((serviceProvider, httpClient) =>
            {
                httpClient.BaseAddress = new Uri(serviceProvider.GetRequiredService<IOptions<CloudflareSettings>>().Value.ApiBaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(30);
            });

            return services;
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
            services.TryAddScoped<ITenantSecretStore>(serviceProvider => new ConfigurationTenantSecretStore(configuration,
                serviceProvider.GetRequiredService<IMultiTenantContextAccessor<ElysianTenantInfo>>()));
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
            services.Configure<PlatformEmailSettings>(configuration.GetSection(PlatformEmailSettings.SectionName));
            services.TryAddSingleton(TimeProvider.System);
            // AddElysianFeatures replaces this with the per-tenant reader; without it the host's values are used
            services.TryAddScoped(typeof(ITenantSettings<>), typeof(HostTenantSettings<>));
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

            services.AddScoped<IGoogleTokenStore, DbGoogleTokenStore>();
            services.AddSingleton<IGoogleOAuthClient, GoogleOAuthClient>();
            services.AddScoped<IGoogleAccessTokenProvider, GoogleAccessTokenProvider>();
            services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
            services.AddScoped<BookingAvailabilityService>();

            return services;
        }
    }
}
