using System.Security.Claims;
using Elysian.Application.Behaviors;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Platform.Commands;
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

        private Task<CreatedTenant> CreateAsync(CreateTenantCommand command) =>
            new CreateTenantCommandHandler(platform, admin, TimeProvider.System, Options.Create(settings)).Handle(command, CancellationToken.None);

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
