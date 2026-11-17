using Elysian.Application.Exceptions;
using Elysian.Application.Features.Platform.Commands;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.Platforms
{
    public sealed class EdgeClientTests : IDisposable
    {
        private static readonly ElysianTenantInfo Aspen = new() { Id = "t-aspen", Identifier = "aspensmitha", Platform = PlatformNames.Photography, Runtime = RuntimeNames.Custom };
        private static readonly ElysianTenantInfo Friend = new() { Id = "t-friend", Identifier = "friend", Platform = PlatformNames.Photography, Runtime = RuntimeNames.Cloud };
        private static readonly ElysianTenantInfo Rob = new() { Id = "t-rob", Identifier = "robsmitha", Platform = PlatformNames.Personal, Runtime = RuntimeNames.Custom };

        private readonly ServiceProvider tenants;
        private readonly PlatformContext context;
        private readonly Clock clock = new();

        public EdgeClientTests()
        {
            var services = new ServiceCollection();
            services.AddMultiTenant<ElysianTenantInfo>().WithInMemoryStore(options =>
            {
                options.Tenants.Add(Aspen);
                options.Tenants.Add(Friend);
                options.Tenants.Add(Rob);
            });
            tenants = services.BuildServiceProvider();

            context = new PlatformContext(new DbContextOptionsBuilder<PlatformContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        }

        public void Dispose()
        {
            context.Dispose();
            tenants.Dispose();
        }

        // ---- Keys

        [Fact]
        public void A_credential_carries_the_client_name_and_a_key_that_matches_its_hash()
        {
            var key = EdgeClientKeys.GenerateKey();
            var credential = EdgeClientKeys.Format("site-aspensmitha", key);

            Assert.True(EdgeClientKeys.TryParse(credential, out var name, out var parsedKey));
            Assert.Equal("site-aspensmitha", name);
            Assert.Equal(key, parsedKey);
            Assert.True(EdgeClientKeys.Matches(parsedKey, EdgeClientKeys.Hash(key)));
            Assert.False(EdgeClientKeys.Matches(parsedKey + "x", EdgeClientKeys.Hash(key)));
            Assert.NotEqual(key, EdgeClientKeys.GenerateKey());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("no-dot")]
        [InlineData(".key")]
        [InlineData("name.")]
        [InlineData("Bad_Name.key")]
        public void Malformed_credentials_dont_parse(string? credential)
        {
            Assert.False(EdgeClientKeys.TryParse(credential, out _, out _));
        }

        // ---- Scope

        [Fact]
        public async Task A_site_client_may_act_for_its_own_tenant_only()
        {
            var credential = await AddClientAsync("site-aspensmitha", EdgeClientScope.Tenant, tenantIdentifier: "aspensmitha");
            var validator = Validator();

            var own = await validator.AuthorizeAsync(credential, "aspensmitha");
            var other = await validator.AuthorizeAsync(credential, "robsmitha");

            Assert.True(own.IsAllowed);
            Assert.False(own.RequiresMembership);
            Assert.Equal(EdgeAccess.TenantNotAllowed, other.Access);
            Assert.Equal("site-aspensmitha", other.ClientName);
        }

        [Fact]
        public async Task A_platform_client_may_act_for_that_platforms_cloud_tenants_only()
        {
            var credential = await AddClientAsync("cloud-photography", EdgeClientScope.Platform, platform: PlatformNames.Photography);
            var validator = Validator();

            Assert.True((await validator.AuthorizeAsync(credential, "friend")).IsAllowed);
            Assert.Equal(EdgeAccess.TenantNotAllowed, (await validator.AuthorizeAsync(credential, "aspensmitha")).Access);
            Assert.Equal(EdgeAccess.TenantNotAllowed, (await validator.AuthorizeAsync(credential, "robsmitha")).Access);
        }

        [Fact]
        public async Task The_admin_client_may_name_any_tenant_but_flags_that_membership_must_be_checked()
        {
            var credential = await AddClientAsync("admin", EdgeClientScope.Admin);

            var result = await Validator().AuthorizeAsync(credential, "robsmitha");

            Assert.True(result.IsAllowed);
            Assert.True(result.RequiresMembership);
        }

        // ---- Refusals

        [Fact]
        public async Task A_missing_wrong_or_unknown_credential_is_refused()
        {
            var credential = await AddClientAsync("site-aspensmitha", EdgeClientScope.Tenant, tenantIdentifier: "aspensmitha");
            var validator = Validator();

            Assert.Equal(EdgeAccess.MissingCredential, (await validator.AuthorizeAsync(null, "aspensmitha")).Access);
            Assert.Equal(EdgeAccess.InvalidCredential, (await validator.AuthorizeAsync(credential + "x", "aspensmitha")).Access);
            Assert.Equal(EdgeAccess.InvalidCredential, (await validator.AuthorizeAsync("site-nobody.whatever", "aspensmitha")).Access);
            Assert.Equal(EdgeAccess.InvalidCredential, (await validator.AuthorizeAsync("not a credential", "aspensmitha")).Access);
        }

        [Fact]
        public async Task A_tenant_that_doesnt_exist_is_refused()
        {
            var credential = await AddClientAsync("admin", EdgeClientScope.Admin);
            var validator = Validator();

            Assert.Equal(EdgeAccess.UnknownTenant, (await validator.AuthorizeAsync(credential, "nobody")).Access);
            Assert.Equal(EdgeAccess.UnknownTenant, (await validator.AuthorizeAsync(credential, null)).Access);
        }

        [Fact]
        public async Task A_disabled_client_is_refused()
        {
            var credential = await AddClientAsync("site-aspensmitha", EdgeClientScope.Tenant, tenantIdentifier: "aspensmitha", enabled: false);

            Assert.Equal(EdgeAccess.InvalidCredential, (await Validator().AuthorizeAsync(credential, "aspensmitha")).Access);
        }

        // ---- Rotation

        [Fact]
        public async Task After_rotation_both_keys_work_until_the_old_one_is_retired()
        {
            using var host = new TestHost();
            var created = await new CreateEdgeClientCommandHandler(host.Context)
                .Handle(new CreateEdgeClientCommand("site-aspensmitha", EdgeClientScope.Tenant, "aspensmitha"), CancellationToken.None);
            var rotated = await new RotateEdgeClientKeyCommandHandler(host.Context)
                .Handle(new RotateEdgeClientKeyCommand("site-aspensmitha"), CancellationToken.None);

            await CopyClientsAsync(host);
            Assert.NotEqual(created.Credential, rotated.Credential);
            Assert.True((await Validator().AuthorizeAsync(created.Credential, "aspensmitha")).IsAllowed);
            Assert.True((await Validator().AuthorizeAsync(rotated.Credential, "aspensmitha")).IsAllowed);

            await new RetirePreviousEdgeClientKeyCommandHandler(host.Context)
                .Handle(new RetirePreviousEdgeClientKeyCommand("site-aspensmitha"), CancellationToken.None);

            await CopyClientsAsync(host);
            Assert.Equal(EdgeAccess.InvalidCredential, (await Validator().AuthorizeAsync(created.Credential, "aspensmitha")).Access);
            Assert.True((await Validator().AuthorizeAsync(rotated.Credential, "aspensmitha")).IsAllowed);
        }

        [Fact]
        public async Task A_host_with_the_old_key_cached_accepts_the_new_one_after_a_short_wait()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var oldCredential = await AddClientAsync("site-aspensmitha", EdgeClientScope.Tenant, tenantIdentifier: "aspensmitha");
            Assert.True((await Validator(cache).AuthorizeAsync(oldCredential, "aspensmitha")).IsAllowed);

            var newKey = EdgeClientKeys.GenerateKey();
            var client = await context.EdgeClients.SingleAsync();
            client.PreviousKeyHash = client.KeyHash;
            client.KeyHash = EdgeClientKeys.Hash(newKey);
            await context.SaveChangesAsync();
            var newCredential = EdgeClientKeys.Format("site-aspensmitha", newKey);

            // Straight away the cached client doesn't know the new key, and isn't re-read yet
            Assert.Equal(EdgeAccess.InvalidCredential, (await Validator(cache).AuthorizeAsync(newCredential, "aspensmitha")).Access);

            clock.Advance(TimeSpan.FromSeconds(6));
            Assert.True((await Validator(cache).AuthorizeAsync(newCredential, "aspensmitha")).IsAllowed);
        }

        // ---- Commands

        [Fact]
        public async Task The_key_is_returned_once_and_only_its_hash_is_stored()
        {
            using var host = new TestHost();

            var created = await new CreateEdgeClientCommandHandler(host.Context)
                .Handle(new CreateEdgeClientCommand("cloud-photography", EdgeClientScope.Platform, Platform: "Photography"), CancellationToken.None);

            var stored = await host.Context.EdgeClients.SingleAsync();
            Assert.True(EdgeClientKeys.TryParse(created.Credential, out _, out var key));
            Assert.Equal(EdgeClientKeys.Hash(key), stored.KeyHash);
            Assert.Equal(PlatformNames.Photography, stored.Platform);
            Assert.DoesNotContain(key, created.ToString());

            await Assert.ThrowsAsync<ConflictException>(() => new CreateEdgeClientCommandHandler(host.Context)
                .Handle(new CreateEdgeClientCommand("cloud-photography", EdgeClientScope.Admin), CancellationToken.None));
        }

        [Theory]
        [InlineData("site-a", EdgeClientScope.Tenant, "aspensmitha", null, true)]
        [InlineData("site-a", EdgeClientScope.Tenant, null, null, false)]
        [InlineData("site-a", EdgeClientScope.Tenant, "aspensmitha", "photography", false)]
        [InlineData("cloud-p", EdgeClientScope.Platform, null, "photography", true)]
        [InlineData("cloud-p", EdgeClientScope.Platform, null, "personal", false)]
        [InlineData("cloud-p", EdgeClientScope.Platform, null, "unknown", false)]
        [InlineData("admin", EdgeClientScope.Admin, null, null, true)]
        [InlineData("admin", EdgeClientScope.Admin, "aspensmitha", null, false)]
        [InlineData("Bad Name", EdgeClientScope.Admin, null, null, false)]
        public void A_client_must_name_exactly_what_its_scope_needs(string name, EdgeClientScope scope, string? tenant, string? platform, bool valid)
        {
            var result = new CreateEdgeClientCommandValidator().Validate(new CreateEdgeClientCommand(name, scope, tenant, platform));

            Assert.Equal(valid, result.IsValid);
        }

        private EdgeClientValidator Validator(IMemoryCache? cache = null) => new(
            context,
            tenants.GetRequiredService<IMultiTenantStore<ElysianTenantInfo>>(),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            clock,
            Options.Create(new EdgeSettings()));

        private async Task<string> AddClientAsync(string name, EdgeClientScope scope, string? tenantIdentifier = null, string? platform = null, bool enabled = true)
        {
            var key = EdgeClientKeys.GenerateKey();
            context.EdgeClients.Add(new EdgeClient
            {
                Name = name,
                Scope = scope,
                TenantIdentifier = tenantIdentifier,
                Platform = platform,
                KeyHash = EdgeClientKeys.Hash(key),
                IsEnabled = enabled,
                CreatedByUserId = "test",
                ModifiedByUserId = "test",
            });
            await context.SaveChangesAsync();
            return EdgeClientKeys.Format(name, key);
        }

        /// <summary>
        /// Commands write through ElysianContext and the validator reads through PlatformContext; in production
        /// both are the same table, here they're separate in-memory databases.
        /// </summary>
        private async Task CopyClientsAsync(TestHost host)
        {
            context.EdgeClients.RemoveRange(context.EdgeClients);
            await context.SaveChangesAsync();
            foreach (var client in await host.Context.EdgeClients.AsNoTracking().ToListAsync())
            {
                client.EdgeClientId = 0;
                context.EdgeClients.Add(client);
            }
            await context.SaveChangesAsync();
        }

        private sealed class Clock : TimeProvider
        {
            private DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => now;

            public void Advance(TimeSpan by) => now += by;
        }
    }
}
