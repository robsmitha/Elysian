using Elysian.Application.Exceptions;
using Elysian.Application.Features.Platform.Commands;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Tests.Platforms
{
    public sealed class TenantDomainTests : IDisposable
    {
        private static readonly ElysianTenantInfo Aspen = new() { Id = "t-aspen", Identifier = "aspensmitha" };
        private static readonly ElysianTenantInfo Friend = new() { Id = "t-friend", Identifier = "friend" };

        private readonly PlatformContext platform;
        private readonly ClaimsPrincipalAccessor admin = new() { Principal = TestHost.Principal("platform-admin", "admin@example.com") };

        public TenantDomainTests()
        {
            platform = new PlatformContext(new DbContextOptionsBuilder<PlatformContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            platform.Tenants.AddRange(Aspen with { }, Friend with { });
            platform.SaveChanges();
        }

        public void Dispose() => platform.Dispose();

        // ---- Hosts

        [Theory]
        [InlineData("www.example.com")]
        [InlineData("example.com")]
        [InlineData("friend.framenice.com")]
        [InlineData("a-b.c1.example.co.uk")]
        public void A_bare_dns_name_is_a_host(string host) => Assert.True(TenantHosts.IsWellFormed(host));

        [Theory]
        [InlineData("")]
        [InlineData("localhost")]
        [InlineData("https://example.com")]
        [InlineData("example.com/path")]
        [InlineData("example.com:8080")]
        [InlineData("-bad.example.com")]
        [InlineData("bad-.example.com")]
        [InlineData("exa mple.com")]
        [InlineData("example..com")]
        [InlineData("*.example.com")]
        [InlineData("user@example.com")]
        [InlineData("192.168.1.10")]
        [InlineData("bücher.example.com")]
        public void Anything_else_is_not(string host) => Assert.False(TenantHosts.IsWellFormed(TenantHosts.Normalize(host)));

        [Fact]
        public void A_host_is_stored_in_one_spelling()
        {
            Assert.Equal("www.example.com", TenantHosts.Normalize("  WWW.Example.COM. "));
            Assert.Equal(string.Empty, TenantHosts.Normalize(null));
            Assert.False(TenantHosts.IsWellFormed(new string('a', 63) + "." + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 63) + ".com"));
        }

        [Fact]
        public void A_subdomain_address_carries_the_sites_own_name()
        {
            Assert.True(TenantHosts.IsSubdomainOf("friend.framenice.com", "friend"));
            Assert.False(TenantHosts.IsSubdomainOf("manage.framenice.com", "friend"));
            Assert.False(TenantHosts.IsSubdomainOf("friend.com", "friend"));
        }

        // ---- Adding

        [Fact]
        public async Task A_sites_first_address_becomes_its_primary_one()
        {
            var added = await AddAsync(new AddTenantDomainCommand("Friend", " Friend.FrameNice.com. ", "subdomain"));

            Assert.Equal(new TenantDomainModel("friend.framenice.com", TenantDomainKinds.Subdomain, TenantDomainStatuses.Pending, true), added);

            var row = await platform.TenantDomains.SingleAsync();
            Assert.Equal(Friend.Id, row.TenantId);
            Assert.Equal("platform-admin", row.CreatedByUserId);
        }

        [Fact]
        public async Task A_later_address_is_not_primary_unless_asked()
        {
            await AddAsync(new AddTenantDomainCommand("friend", "friend.framenice.com", "subdomain", "active"));
            var second = await AddAsync(new AddTenantDomainCommand("friend", "www.friendphoto.com", "custom"));
            Assert.False(second.IsPrimary);

            var third = await AddAsync(new AddTenantDomainCommand("friend", "friendphoto.com", "custom", MakePrimary: true));
            Assert.True(third.IsPrimary);

            var domains = await ListAsync("friend");
            Assert.Equal(["friendphoto.com", "friend.framenice.com", "www.friendphoto.com"], domains.Select(d => d.Host));
            Assert.Single(domains, d => d.IsPrimary);
        }

        [Fact]
        public async Task An_address_belongs_to_one_site_only()
        {
            await AddAsync(new AddTenantDomainCommand("aspensmitha", "www.aspensmitha.com", "custom", "active"));

            var other = await Assert.ThrowsAsync<ConflictException>(() => AddAsync(new AddTenantDomainCommand("friend", "WWW.aspensmitha.com", "custom")));
            Assert.Contains("another site", other.Message);

            var same = await Assert.ThrowsAsync<ConflictException>(() => AddAsync(new AddTenantDomainCommand("aspensmitha", "www.aspensmitha.com", "custom")));
            Assert.Contains("already one of this site's", same.Message);
        }

        [Fact]
        public async Task A_removed_address_can_be_given_to_another_site()
        {
            await AddAsync(new AddTenantDomainCommand("friend", "www.shared.com", "custom"));
            await RemoveAsync("friend", "www.shared.com");

            var added = await AddAsync(new AddTenantDomainCommand("aspensmitha", "www.shared.com", "custom"));
            Assert.True(added.IsPrimary);
        }

        [Theory]
        [InlineData("https://www.example.com", "custom", null)]
        [InlineData("www.example.com", "wildcard", null)]
        [InlineData("www.example.com", "custom", "live")]
        [InlineData("manage.framenice.com", "subdomain", null)]
        [InlineData("friend.com", "subdomain", null)]
        public void An_address_that_is_not_one_is_refused(string host, string kind, string? status)
        {
            var result = new AddTenantDomainCommandValidator().Validate(new AddTenantDomainCommand("friend", host, kind, status));
            Assert.False(result.IsValid);
        }

        [Fact]
        public void A_good_address_passes_validation()
        {
            Assert.True(new AddTenantDomainCommandValidator().Validate(new AddTenantDomainCommand("friend", "friend.framenice.com", "subdomain", "active")).IsValid);
            Assert.True(new AddTenantDomainCommandValidator().Validate(new AddTenantDomainCommand("friend", "www.friendphoto.com", "custom")).IsValid);
        }

        [Fact]
        public async Task An_unknown_site_has_no_addresses_to_manage()
        {
            await Assert.ThrowsAsync<NotFoundException>(() => AddAsync(new AddTenantDomainCommand("nobody", "www.example.com", "custom")));
            await Assert.ThrowsAsync<NotFoundException>(() => ListAsync("nobody"));
        }

        // ---- Changing and removing

        [Fact]
        public async Task Making_an_address_primary_takes_it_from_the_one_that_was()
        {
            await AddAsync(new AddTenantDomainCommand("friend", "friend.framenice.com", "subdomain", "active"));
            await AddAsync(new AddTenantDomainCommand("friend", "www.friendphoto.com", "custom"));

            var updated = await UpdateAsync(new UpdateTenantDomainCommand("friend", "www.friendphoto.com", "active", MakePrimary: true));
            Assert.Equal(new TenantDomainModel("www.friendphoto.com", TenantDomainKinds.Custom, TenantDomainStatuses.Active, true), updated);

            var domains = await ListAsync("friend");
            Assert.Equal("www.friendphoto.com", Assert.Single(domains, d => d.IsPrimary).Host);
        }

        [Fact]
        public async Task The_primary_address_cannot_simply_be_unset()
        {
            var result = new UpdateTenantDomainCommandValidator().Validate(new UpdateTenantDomainCommand("friend", "friend.framenice.com", MakePrimary: false));
            Assert.False(result.IsValid);

            await AddAsync(new AddTenantDomainCommand("friend", "friend.framenice.com", "subdomain"));
            await Assert.ThrowsAsync<NotFoundException>(() => UpdateAsync(new UpdateTenantDomainCommand("friend", "www.unknown.com", "active")));
        }

        [Fact]
        public async Task The_primary_address_goes_last()
        {
            await AddAsync(new AddTenantDomainCommand("friend", "friend.framenice.com", "subdomain"));
            await AddAsync(new AddTenantDomainCommand("friend", "www.friendphoto.com", "custom"));

            await Assert.ThrowsAsync<ConflictException>(() => RemoveAsync("friend", "friend.framenice.com"));

            await RemoveAsync("friend", "www.friendphoto.com");
            await RemoveAsync("friend", "friend.framenice.com");
            Assert.Empty(await ListAsync("friend"));
        }

        [Fact]
        public async Task One_sites_addresses_are_not_anothers()
        {
            await AddAsync(new AddTenantDomainCommand("aspensmitha", "www.aspensmitha.com", "custom"));
            await AddAsync(new AddTenantDomainCommand("friend", "friend.framenice.com", "subdomain"));

            Assert.Equal(["www.aspensmitha.com"], (await ListAsync("aspensmitha")).Select(d => d.Host));
            await Assert.ThrowsAsync<NotFoundException>(() => RemoveAsync("friend", "www.aspensmitha.com"));
        }

        [Fact]
        public void A_site_cannot_be_named_preview()
        {
            Assert.True(TenantIdentifiers.IsReserved("preview"));
        }

        private Task<TenantDomainModel> AddAsync(AddTenantDomainCommand command)
        {
            new AddTenantDomainCommandValidator().ValidateAndThrow(command);
            return new AddTenantDomainCommandHandler(platform, admin, TimeProvider.System).Handle(command, CancellationToken.None);
        }

        private Task<TenantDomainModel> UpdateAsync(UpdateTenantDomainCommand command)
        {
            new UpdateTenantDomainCommandValidator().ValidateAndThrow(command);
            return new UpdateTenantDomainCommandHandler(platform, admin, TimeProvider.System).Handle(command, CancellationToken.None);
        }

        private Task RemoveAsync(string identifier, string host) =>
            new RemoveTenantDomainCommandHandler(platform, admin, TimeProvider.System).Handle(new RemoveTenantDomainCommand(identifier, host), CancellationToken.None);

        private Task<List<TenantDomainModel>> ListAsync(string identifier) =>
            new GetTenantDomainsQueryHandler(platform).Handle(new GetTenantDomainsQuery(identifier), CancellationToken.None);
    }
}
