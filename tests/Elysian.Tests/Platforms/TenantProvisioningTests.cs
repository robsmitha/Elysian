using System.Security.Claims;
using Elysian.Application.Behaviors;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Platform.Commands;
using Elysian.Application.Features.SiteContent;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Identity;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.Platforms
{
    public sealed class TenantProvisioningTests : IDisposable
    {
        private const string AdminSubject = "platform-admin-oid";

        private readonly PlatformContext platform;
        private readonly AuthSettings settings = new() { RoleSource = RoleSource.Membership, PlatformAdminSubjects = [AdminSubject] };
        private readonly ClaimsPrincipalAccessor admin = new() { Principal = Person(AdminSubject) };

        public TenantProvisioningTests()
        {
            platform = new PlatformContext(new DbContextOptionsBuilder<PlatformContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            platform.MerchantTypes.Add(PlatformContext.Stamp(new MerchantType { MerchantTypeId = 1, Name = "Portfolio", Description = "Portfolio" }, "seed", DateTimeOffset.UtcNow));
            platform.MerchantTypes.Add(PlatformContext.Stamp(new MerchantType { MerchantTypeId = 2, Name = "Store", Description = "Store" }, "seed", DateTimeOffset.UtcNow));
            platform.SaveChanges();
        }

        public void Dispose() => platform.Dispose();

        // ---- Creating

        [Fact]
        public async Task A_new_site_gets_everything_it_needs_in_one_step()
        {
            var created = await CreateAsync(new CreateTenantCommand(" Friend-Photo ", "Friend Photography", "photography", "cloud", "Owner@Example.com"));

            Assert.Equal("friend-photo", created.Identifier);
            Assert.Equal([ModuleNames.Photos], created.Modules);

            var tenant = await platform.Tenants.SingleAsync();
            Assert.Equal(PlatformNames.Photography, tenant.Platform);
            Assert.Equal(RuntimeNames.Cloud, tenant.Runtime);
            Assert.Equal(TenantStatusNames.Active, tenant.Status);
            // A cloud site starts on its platform's default template, at the newest version the host has
            Assert.Equal(("classic", 2), (tenant.TemplateKey, tenant.TemplateVersion));
            Assert.Equal(("classic", 2), (created.TemplateKey, created.TemplateVersion));
            Assert.Null(tenant.ContentVersion);

            var merchant = await platform.Merchants.SingleAsync();
            Assert.Equal("friend-photo", merchant.MerchantIdentifier);
            Assert.Equal(2, merchant.MerchantTypeId);

            var module = await platform.TenantModules.SingleAsync();
            Assert.Equal(tenant.Id, module.TenantId);
            Assert.Equal(ModuleNames.Photos, module.ModuleKey);

            var invite = await platform.TenantInvites.SingleAsync();
            Assert.Equal(tenant.Id, invite.TenantId);
            Assert.Equal(RoleNames.Owner, invite.Role);
            Assert.Equal("owner@example.com", invite.Email);
            Assert.Equal(InviteCodes.Hash(created.OwnerInviteCode), invite.CodeHash);
            Assert.DoesNotContain(created.OwnerInviteCode, created.ToString());
        }

        [Fact]
        public async Task A_personal_site_starts_with_no_modules()
        {
            var created = await CreateAsync(new CreateTenantCommand("someone", "Someone", "personal", "custom"));

            Assert.Empty(created.Modules);
            Assert.Empty(platform.TenantModules);
            Assert.Equal(1, (await platform.Merchants.SingleAsync()).MerchantTypeId);
        }

        [Fact]
        public async Task A_name_thats_taken_is_refused_and_nothing_is_half_made()
        {
            await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"));

            await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(new CreateTenantCommand("FRIEND", "Another", "photography", "custom")));

            Assert.Single(platform.Tenants);
            Assert.Single(platform.Merchants);
            Assert.Single(platform.TenantInvites);
        }

        [Theory]
        [InlineData("friend-photo", "Friend", "photography", "cloud", null, true)]
        [InlineData("friend", "Friend", "Photography", "Custom", "owner@example.com", true)]
        [InlineData("rob", "Rob", "personal", "custom", null, true)]
        [InlineData("rob", "Rob", "personal", "cloud", null, false)]
        [InlineData("www", "Friend", "photography", "cloud", null, false)]
        [InlineData("Manage", "Friend", "photography", "cloud", null, false)]
        [InlineData("images", "Friend", "photography", "cloud", null, false)]
        [InlineData("ab", "Friend", "photography", "cloud", null, false)]
        [InlineData("-friend", "Friend", "photography", "cloud", null, false)]
        [InlineData("friend_photo", "Friend", "photography", "cloud", null, false)]
        [InlineData("friend.photo", "Friend", "photography", "cloud", null, false)]
        [InlineData("xn--friend", "Friend", "photography", "cloud", null, false)]
        [InlineData("friend", "", "photography", "cloud", null, false)]
        [InlineData("friend", "Friend", "shop", "cloud", null, false)]
        [InlineData("friend", "Friend", "photography", "hosted", null, false)]
        [InlineData("friend", "Friend", "photography", "cloud", "not an email", false)]
        public void A_site_must_have_a_usable_name_a_real_platform_and_a_runtime_it_supports(
            string identifier, string name, string platformKey, string runtime, string? ownerEmail, bool valid)
        {
            var result = new CreateTenantCommandValidator().Validate(new CreateTenantCommand(identifier, name, platformKey, runtime, ownerEmail));

            Assert.Equal(valid, result.IsValid);
        }

        // ---- Status and modules

        [Fact]
        public async Task A_suspended_site_is_refused_at_the_edge_except_from_the_admin_app()
        {
            await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"));
            var cloud = AddClient("cloud-photography", EdgeClientScope.Platform, platformKey: PlatformNames.Photography);
            var adminApp = AddClient("admin", EdgeClientScope.Admin);
            await platform.SaveChangesAsync();
            Assert.True((await Validator().AuthorizeAsync(cloud, "friend")).IsAllowed);

            await new SetTenantStatusCommandHandler(platform).Handle(new SetTenantStatusCommand("friend", "Suspended"), CancellationToken.None);

            Assert.Equal(EdgeAccess.TenantSuspended, (await Validator().AuthorizeAsync(cloud, "friend")).Access);
            Assert.True((await Validator().AuthorizeAsync(adminApp, "friend")).IsAllowed);

            await new SetTenantStatusCommandHandler(platform).Handle(new SetTenantStatusCommand("friend", "active"), CancellationToken.None);
            Assert.True((await Validator().AuthorizeAsync(cloud, "friend")).IsAllowed);

            Assert.False(new SetTenantStatusCommandValidator().Validate(new SetTenantStatusCommand("friend", "deleted")).IsValid);
            await Assert.ThrowsAsync<NotFoundException>(() =>
                new SetTenantStatusCommandHandler(platform).Handle(new SetTenantStatusCommand("nobody", "active"), CancellationToken.None));
        }

        [Fact]
        public async Task An_add_on_can_be_switched_on_and_off_but_only_if_the_platform_offers_it()
        {
            await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"));
            var handler = new SetTenantModuleCommandHandler(platform, admin, TimeProvider.System);

            await handler.Handle(new SetTenantModuleCommand("friend", "Booking", true, "sub_123"), CancellationToken.None);
            var booking = await platform.TenantModules.SingleAsync(m => m.ModuleKey == ModuleNames.Booking);
            Assert.Equal(TenantModuleStatus.Active, booking.Status);
            Assert.Equal("sub_123", booking.BillingReference);

            await handler.Handle(new SetTenantModuleCommand("friend", ModuleNames.Booking, false), CancellationToken.None);
            Assert.Equal(TenantModuleStatus.Suspended, booking.Status);

            await handler.Handle(new SetTenantModuleCommand("friend", ModuleNames.Booking, true), CancellationToken.None);
            Assert.Equal(TenantModuleStatus.Active, booking.Status);
            Assert.Equal("sub_123", booking.BillingReference);
            Assert.Equal(2, await platform.TenantModules.CountAsync());

            await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new SetTenantModuleCommand("friend", ModuleNames.Finance, true), CancellationToken.None));
            await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new SetTenantModuleCommand("friend", "no-such-module", true), CancellationToken.None));
        }

        // ---- Who may do this

        [Fact]
        public async Task Only_a_platform_admin_may_send_platform_requests()
        {
            using var host = new TestHost();
            var identity = new IdentityService(host.Context, host.TenantAccessor, TimeProvider.System, NullLogger<IdentityService>.Instance, Options.Create(settings));
            var request = new CreateTenantCommand("friend", "Friend", "photography", "cloud");
            var handled = 0;
            Task<CreatedTenant> Next() { handled++; return Task.FromResult<CreatedTenant>(null!); }

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Behavior(new ClaimsPrincipalAccessor { Principal = new ClaimsPrincipal() }, identity).Handle(request, Next, CancellationToken.None));
            await Assert.ThrowsAsync<ForbiddenAccessException>(() => Behavior(new ClaimsPrincipalAccessor { Principal = Person("a-site-owner") }, identity).Handle(request, Next, CancellationToken.None));
            Assert.Equal(0, handled);

            await Behavior(admin, identity).Handle(request, Next, CancellationToken.None);
            Assert.Equal(1, handled);
        }

        [Fact]
        public void A_host_that_names_no_platform_admins_has_none()
        {
            using var host = new TestHost();
            var identity = new IdentityService(host.Context, host.TenantAccessor, TimeProvider.System, NullLogger<IdentityService>.Instance, Options.Create(new AuthSettings()));

            Assert.False(identity.IsPlatformAdmin(Person(AdminSubject)));
            Assert.False(identity.IsPlatformAdmin(null));
        }

        private static AuthorizationBehavior<CreateTenantCommand, CreatedTenant> Behavior(IClaimsPrincipalAccessor accessor, IIdentityService identity) =>
            new(accessor, identity);

        private Task<CreatedTenant> CreateAsync(CreateTenantCommand command, SiteTemplateCatalog? templates = null) =>
            new CreateTenantCommandHandler(platform, admin, TimeProvider.System, Options.Create(settings), templates ?? Templates, publishQueue,
                    domains: Options.Create(domains), hostMap: hostMap)
                .Handle(command, CancellationToken.None);

        // ---- A new site's address

        private readonly PlatformDomainSettings domains = new();
        private readonly RecordingHostMap hostMap = new();

        private sealed class RecordingHostMap : IEdgeHostMap
        {
            public List<string> Synced { get; } = [];
            public bool Fail { get; set; }

            public Task SyncAsync(string tenantIdentifier, CancellationToken cancellationToken)
            {
                if (Fail)
                {
                    throw new InvalidOperationException("The edge could not be reached.");
                }
                Synced.Add(tenantIdentifier);
                return Task.CompletedTask;
            }

            public Task RemoveAsync(string host, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        [Fact]
        public async Task A_cloud_site_answers_on_its_own_name_under_its_platforms_domain_from_the_start()
        {
            domains.Domains["Photography"] = " FrameNice.example. ";

            var created = await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"));

            Assert.Equal("friend.framenice.example", created.Address);
            var domain = await platform.TenantDomains.SingleAsync();
            Assert.Equal(("friend.framenice.example", TenantDomainKinds.Subdomain, TenantDomainStatuses.Active, true),
                (domain.Host, domain.Kind, domain.Status, domain.IsPrimary));
            Assert.Equal((await platform.Tenants.SingleAsync()).Id, domain.TenantId);
            // The edge is told, so the address answers
            Assert.Equal(["friend"], hostMap.Synced);
        }

        [Fact]
        public async Task A_site_gets_no_address_when_its_platform_has_no_domain_or_it_has_its_own_code()
        {
            Assert.Null((await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"))).Address);

            domains.Domains["photography"] = "framenice.example";
            domains.Domains["personal"] = "personal.example";
            Assert.Null((await CreateAsync(new CreateTenantCommand("own-code", "Own code", "photography", "custom"))).Address);
            Assert.Null((await CreateAsync(new CreateTenantCommand("someone", "Someone", "personal", "custom"))).Address);

            Assert.Empty(platform.TenantDomains);
            Assert.Empty(hostMap.Synced);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not a domain")]
        [InlineData("https://framenice.example")]
        [InlineData("framenice.example/path")]
        public async Task A_domain_that_is_not_one_gives_no_address(string configured)
        {
            domains.Domains["photography"] = configured;

            Assert.Null((await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"))).Address);
            Assert.Empty(platform.TenantDomains);
        }

        [Fact]
        public async Task A_site_is_still_created_when_the_edge_cannot_be_told_or_its_address_is_taken()
        {
            domains.Domains["photography"] = "framenice.example";

            hostMap.Fail = true;
            var first = await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"));
            Assert.Equal("friend.framenice.example", first.Address);
            Assert.Single(platform.TenantDomains);

            // Somebody gave the next site's address to this one by hand
            hostMap.Fail = false;
            platform.TenantDomains.Add(PlatformContext.Stamp(new TenantDomain
            {
                TenantId = (await platform.Tenants.SingleAsync()).Id!, Host = "other.framenice.example",
                Kind = TenantDomainKinds.Custom, Status = TenantDomainStatuses.Active,
            }, "seed", DateTimeOffset.UtcNow));
            await platform.SaveChangesAsync();

            var second = await CreateAsync(new CreateTenantCommand("other", "Other", "photography", "cloud"));
            Assert.Null(second.Address);
            Assert.Equal(2, await platform.Tenants.CountAsync());
            Assert.Equal(2, await platform.TenantDomains.CountAsync());
        }

        private readonly RecordingPublishQueue publishQueue = new();

        private sealed class RecordingPublishQueue : Elysian.Application.Interfaces.IPlacementPublishQueue
        {
            public List<Elysian.Application.Features.Placements.Models.PlacementPublishMessage> Sent { get; } = [];
            public bool Fail { get; set; }

            public Task EnqueueAsync(string reason, bool force = false) => throw new NotSupportedException("There is no current tenant when a site is created");

            public Task SendAsync(Elysian.Application.Features.Placements.Models.PlacementPublishMessage message, TimeSpan? delay = null, CancellationToken cancellationToken = default)
            {
                if (Fail)
                {
                    throw new InvalidOperationException("The queue could not be reached.");
                }
                Sent.Add(message);
                return Task.CompletedTask;
            }
        }

        private const string TemplateManifest = """
            {
              "version": 1,
              "site": { "name": "", "publicUrl": "" },
              "categories": [ { "slug": "weddings", "name": "Weddings" } ],
              "pages": [ { "key": "home", "name": "Home", "path": "/" } ],
              "spots": [ { "key": "home-hero", "label": "Hero", "page": "home" }, { "key": "site-default", "label": "Fallback", "page": "home" } ],
              "placements": { "recipeVersion": 1, "siteDefaultKey": "site-default", "slots": [ { "key": "home-hero", "name": "home-hero", "ratio": 2.2, "widths": [640], "alt": "Featured" } ] }
            }
            """;

        private static readonly SiteTemplateCatalog TemplatesWithImages =
            SiteTemplateCatalog.FromFiles([(TemplateJson("classic", 1), TemplateManifest)]);

        // ---- A new site's images

        [Fact]
        public async Task A_new_cloud_sites_images_are_published_straight_away()
        {
            await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"), TemplatesWithImages);

            var message = Assert.Single(publishQueue.Sent);
            Assert.Equal("friend", message.Tenant);
            Assert.Equal(CreateTenantCommandHandler.PublishReason, message.Reason);
            Assert.True(message.Force);
        }

        [Fact]
        public async Task Nothing_is_published_for_a_site_with_no_reserved_images()
        {
            // Its own code, or a template that ships no manifest
            await CreateAsync(new CreateTenantCommand("someone", "Someone", "photography", "custom"), TemplatesWithImages);
            await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"));

            Assert.Empty(publishQueue.Sent);
        }

        [Fact]
        public async Task A_site_is_still_created_when_its_first_publish_cannot_be_queued()
        {
            publishQueue.Fail = true;
            var created = await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"), TemplatesWithImages);

            Assert.Equal("friend", created.Identifier);
            Assert.Single(await platform.Tenants.ToListAsync());
        }

        private static string TemplateJson(string key, int version) => $$"""
            {
              "key": "{{key}}", "version": {{version}}, "name": "{{key}}",
              "palettes": [ { "key": "paper", "name": "Paper", "background": "#ffffff", "surface": "#f3f5f7", "text": "#16181d", "muted": "#4a5160", "accent": "#1f5f4f", "onAccent": "#ffffff" } ],
              "fonts": [ { "key": "sans", "name": "Sans" } ],
              "fields": [],
              "links": [],
              "categories": [ { "slug": "weddings", "name": "Weddings" } ]
            }
            """;

        private static readonly SiteTemplateCatalog Templates =
            SiteTemplateCatalog.FromJson(TemplateJson("classic", 1), TemplateJson("classic", 2), TemplateJson("gallery", 1));

        // ---- Templates

        [Fact]
        public async Task A_cloud_site_can_ask_for_a_template_by_name()
        {
            var created = await CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud", Template: " Gallery "));
            Assert.Equal(("gallery", 1), (created.TemplateKey, created.TemplateVersion));
        }

        [Fact]
        public async Task A_cloud_site_is_not_created_without_a_template_to_draw_it()
        {
            var unknown = await Assert.ThrowsAsync<CustomValidationException>(() =>
                CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud", Template: "brutalist")));
            Assert.Contains("brutalist", unknown.Errors["Template"].Single());

            await Assert.ThrowsAsync<CustomValidationException>(() =>
                CreateAsync(new CreateTenantCommand("friend", "Friend", "photography", "cloud"), new SiteTemplateCatalog()));

            Assert.Empty(await platform.Tenants.ToListAsync());
            Assert.Empty(await platform.Merchants.ToListAsync());
        }

        [Fact]
        public async Task A_site_with_its_own_code_has_no_template()
        {
            var created = await CreateAsync(new CreateTenantCommand("someone", "Someone", "photography", "custom"));
            Assert.Null(created.TemplateKey);
            Assert.Null((await platform.Tenants.SingleAsync()).TemplateKey);

            await Assert.ThrowsAsync<CustomValidationException>(() =>
                CreateAsync(new CreateTenantCommand("another", "Another", "photography", "custom", Template: "classic")));
        }

        // ---- Listing

        [Fact]
        public async Task Every_site_is_listed_with_what_an_operator_needs_to_see()
        {
            await CreateAsync(new CreateTenantCommand("zebra", "Zebra Photo", "photography", "cloud"));
            await CreateAsync(new CreateTenantCommand("apple", "Apple Studio", "photography", "custom"));

            var zebra = await platform.Tenants.SingleAsync(t => t.Identifier == "zebra");
            zebra.ContentVersion = 3;
            platform.TenantDomains.Add(PlatformContext.Stamp(new TenantDomain
            {
                TenantId = zebra.Id!, Host = "zebra.framenice.com", Kind = TenantDomainKinds.Subdomain, Status = TenantDomainStatuses.Active, IsPrimary = true,
            }, "seed", DateTimeOffset.UtcNow));
            platform.TenantDomains.Add(PlatformContext.Stamp(new TenantDomain
            {
                TenantId = zebra.Id!, Host = "www.zebraphoto.com", Kind = TenantDomainKinds.Custom, Status = TenantDomainStatuses.Pending, IsPrimary = false,
            }, "seed", DateTimeOffset.UtcNow));
            await platform.SaveChangesAsync();

            var tenants = await new GetTenantsQueryHandler(platform).Handle(new GetTenantsQuery(), CancellationToken.None);

            Assert.Equal(["apple", "zebra"], tenants.Select(t => t.Identifier));
            Assert.Equal(new TenantSummary("zebra", "Zebra Photo", "photography", "cloud", "active", "classic", 2, 3, "zebra.framenice.com", [ModuleNames.Photos]) with { Modules = tenants[1].Modules }, tenants[1]);
            Assert.Equal([ModuleNames.Photos], tenants[1].Modules);
            Assert.Null(tenants[0].PrimaryHost);
            Assert.Null(tenants[0].TemplateKey);
        }

        private string AddClient(string name, EdgeClientScope scope, string? platformKey = null)
        {
            var key = EdgeClientKeys.GenerateKey();
            platform.EdgeClients.Add(PlatformContext.Stamp(new EdgeClient
            {
                Name = name, Scope = scope, Platform = platformKey, KeyHash = EdgeClientKeys.Hash(key), IsEnabled = true
            }, "test", DateTimeOffset.UtcNow));
            return EdgeClientKeys.Format(name, key);
        }

        /// <summary>
        /// The tenant store reads the same TenantInfo table the commands write; here it's rebuilt from that table each time
        /// </summary>
        private EdgeClientValidator Validator()
        {
            var services = new ServiceCollection();
            services.AddMultiTenant<ElysianTenantInfo>().WithInMemoryStore(options =>
            {
                foreach (var tenant in platform.Tenants.AsNoTracking().ToList())
                {
                    options.Tenants.Add(tenant);
                }
            });
            var provider = services.BuildServiceProvider();
            return new EdgeClientValidator(platform, provider.GetRequiredService<IMultiTenantStore<ElysianTenantInfo>>(),
                new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, Options.Create(new EdgeSettings()));
        }

        private static ClaimsPrincipal Person(string subject)
        {
            var identity = new ClaimsIdentity(SignInClaims.ExternalIdProvider);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
            return new ClaimsPrincipal(identity);
        }
    }
}
