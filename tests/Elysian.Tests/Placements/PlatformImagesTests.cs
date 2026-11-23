using System.Net;
using System.Text.Json;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Elysian.Tests.Placements
{
    public sealed class PlatformImagesTests
    {
        private static readonly PlatformImagesSettings Platform = new()
        {
            PublicOrigin = "https://images.platform.example/",
            CloudflareZoneId = "platform-zone",
            CloudflareApiToken = "platform-token",
        };

        private static TenantImageHost HostFor(string? runtime, PlatformImagesSettings? settings = null)
        {
            var tenants = new Mock<IMultiTenantContextAccessor<ElysianTenantInfo>>();
            tenants.Setup(t => t.MultiTenantContext.TenantInfo).Returns(new ElysianTenantInfo { Id = "t", Identifier = "friend", Runtime = runtime });
            return new TenantImageHost(Options.Create(settings ?? Platform), tenants.Object);
        }

        // ---- Which host a tenant's images are on

        [Fact]
        public void A_site_drawn_with_a_template_is_on_the_platforms_images_host()
        {
            Assert.Equal("https://images.platform.example/photos", HostFor(RuntimeNames.Cloud).ContainerUrl("photos"));
            Assert.Equal("https://images.platform.example/placements", HostFor(RuntimeNames.Cloud).ContainerUrl("/placements/"));
        }

        [Fact]
        public void A_site_with_its_own_code_keeps_the_host_it_was_built_against()
        {
            Assert.Null(HostFor(RuntimeNames.Custom).ContainerUrl("photos"));
            Assert.Null(HostFor(null).ContainerUrl("photos"));
            // Outside any tenant
            Assert.Null(new TenantImageHost(Options.Create(Platform)).ContainerUrl("photos"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(" ")]
        [InlineData("images.platform.example")]
        [InlineData("http://images.platform.example")]
        public void A_platform_host_that_is_not_an_https_address_is_no_host(string? origin)
        {
            var settings = new PlatformImagesSettings { PublicOrigin = origin, CloudflareZoneId = "z", CloudflareApiToken = "t" };

            Assert.Null(settings.Origin);
            Assert.False(settings.CanPurge);
            Assert.Null(HostFor(RuntimeNames.Cloud, settings).ContainerUrl("photos"));
        }

        // ---- Purging: each address from the zone it is in

        private sealed class Recorder : HttpMessageHandler
        {
            public List<(string Path, string? Token, string[] Files)> Calls { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Calls.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter,
                    [.. body.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetString()!)]));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true}") };
            }
        }

        private static (CloudflareCdnPurger Purger, Recorder Recorder) Purger(CloudflareSettings own, PlatformImagesSettings? platform)
        {
            var recorder = new Recorder();
            var client = new HttpClient(recorder) { BaseAddress = new Uri("https://api.cloudflare.example/client/v4/") };
            return (new CloudflareCdnPurger(client, Options.Create(own), NullLogger<CloudflareCdnPurger>.Instance,
                platform == null ? null : Options.Create(platform)), recorder);
        }

        private static readonly CloudflareSettings Own = new() { ZoneId = "own-zone", ApiToken = "own-token" };

        [Fact]
        public async Task Each_address_is_purged_from_its_own_zone_with_that_zones_token()
        {
            var (purger, recorder) = Purger(Own, Platform);

            await purger.PurgeAsync([
                "https://images.own.example/placements/aspen/home-hero-640.webp",
                "https://images.platform.example/placements/friend/home-hero-640.webp",
                "https://images.platform.example/placements/friend/home-hero-960.webp",
            ]);

            Assert.True(purger.IsConfigured);
            Assert.Equal(2, recorder.Calls.Count);
            var own = recorder.Calls.Single(c => c.Path.EndsWith("/zones/own-zone/purge_cache"));
            Assert.Equal(("own-token", 1), (own.Token, own.Files.Length));
            var platform = recorder.Calls.Single(c => c.Path.EndsWith("/zones/platform-zone/purge_cache"));
            Assert.Equal("platform-token", platform.Token);
            Assert.All(platform.Files, f => Assert.StartsWith("https://images.platform.example/", f));
            Assert.Equal(2, platform.Files.Length);
        }

        [Fact]
        public async Task An_address_that_only_looks_like_the_platforms_is_not_purged_with_its_token()
        {
            var (purger, recorder) = Purger(Own, Platform);

            await purger.PurgeAsync(["https://images.platform.example.evil.test/placements/friend/home-hero-640.webp"]);

            Assert.Equal("own-token", Assert.Single(recorder.Calls).Token);
        }

        [Fact]
        public async Task The_platforms_addresses_are_never_sent_to_another_zone()
        {
            // The platform host is known but has no token yet: its addresses are skipped, not purged from the wrong zone
            var noToken = new PlatformImagesSettings { PublicOrigin = Platform.PublicOrigin };
            var (purger, recorder) = Purger(Own, noToken);

            await purger.PurgeAsync([
                "https://images.platform.example/placements/friend/home-hero-640.webp",
                "https://images.own.example/placements/aspen/home-hero-640.webp",
            ]);

            var call = Assert.Single(recorder.Calls);
            Assert.EndsWith("/zones/own-zone/purge_cache", call.Path);
            Assert.Equal(["https://images.own.example/placements/aspen/home-hero-640.webp"], call.Files);
        }

        [Fact]
        public async Task A_host_with_only_the_platforms_zone_still_purges_it()
        {
            var (purger, recorder) = Purger(new CloudflareSettings(), Platform);

            Assert.True(purger.IsConfigured);
            await purger.PurgeAsync([
                "https://images.platform.example/placements/friend/home-hero-640.webp",
                "https://images.own.example/placements/aspen/home-hero-640.webp",
            ]);

            Assert.EndsWith("/zones/platform-zone/purge_cache", Assert.Single(recorder.Calls).Path);
        }

        [Fact]
        public async Task With_nothing_configured_nothing_is_purged()
        {
            var (purger, recorder) = Purger(new CloudflareSettings(), null);

            Assert.False(purger.IsConfigured);
            await purger.PurgeAsync(["https://images.own.example/placements/aspen/home-hero-640.webp"]);

            Assert.Empty(recorder.Calls);
        }
    }
}
