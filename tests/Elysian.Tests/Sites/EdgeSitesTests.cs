using System.Net;
using System.Text.Json;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Platform.Commands;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Identity;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.Sites
{
    public sealed class EdgeSitesTests : IDisposable
    {
        private static readonly ElysianTenantInfo Friend = new()
        {
            Id = "t-friend", Identifier = "friend", Platform = PlatformNames.Photography, Runtime = RuntimeNames.Cloud,
            Status = TenantStatusNames.Active, TemplateKey = "classic", TemplateVersion = 1, ContentVersion = 4,
        };
        private static readonly ElysianTenantInfo Aspen = new()
        {
            Id = "t-aspen", Identifier = "aspensmitha", Platform = PlatformNames.Photography, Runtime = RuntimeNames.Custom,
            Status = TenantStatusNames.Active,
        };

        private readonly PlatformContext platform;
        private readonly MemoryStore store = new();
        private readonly ClaimsPrincipalAccessor admin = new() { Principal = TestHost.Principal("platform-admin", "admin@example.com") };

        public EdgeSitesTests()
        {
            platform = new PlatformContext(new DbContextOptionsBuilder<PlatformContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            platform.Tenants.AddRange(Friend with { }, Aspen with { });
            platform.SaveChanges();
        }

        public void Dispose() => platform.Dispose();

        private EdgeSites Edge() => new(store, platform, NullLogger<EdgeSites>.Instance);

        // ---- Published content

        [Fact]
        public async Task A_published_version_is_written_under_its_own_key_with_what_the_worker_needs()
        {
            await Edge().WriteVersionAsync("friend", 5, "classic", 1, """{"theme":{"palette":"linen"},"text":{"home.heading":"Hello"}}""", CancellationToken.None);

            var written = Json(store.Values["content:friend:5"]);
            Assert.Equal("friend", written.GetProperty("tenant").GetString());
            Assert.Equal(5, written.GetProperty("version").GetInt32());
            Assert.Equal("classic", written.GetProperty("template").GetString());
            Assert.Equal(1, written.GetProperty("templateVersion").GetInt32());
            Assert.Equal("Hello", written.GetProperty("content").GetProperty("text").GetProperty("home.heading").GetString());
        }

        // ---- The host map

        [Fact]
        public async Task Pointing_at_a_version_writes_every_address_the_primary_one_last()
        {
            await AddAsync("friend", "friend.framenice.com", "subdomain", status: "active");
            await AddAsync("friend", "www.friendphoto.com", "custom", makePrimary: true);
            store.Log.Clear();

            await Edge().PointToVersionAsync("friend", 5, CancellationToken.None);

            Assert.Equal(["put host:friend.framenice.com", "put host:www.friendphoto.com"], store.Log);
            var entry = Json(store.Values["host:friend.framenice.com"]);
            Assert.Equal("friend", entry.GetProperty("tenant").GetString());
            Assert.Equal("active", entry.GetProperty("status").GetString());
            Assert.Equal("classic", entry.GetProperty("template").GetString());
            Assert.Equal(1, entry.GetProperty("templateVersion").GetInt32());
            // The version being published, not the one still recorded on the tenant
            Assert.Equal(5, entry.GetProperty("contentVersion").GetInt32());
            Assert.Equal("www.friendphoto.com", entry.GetProperty("primaryHost").GetString());
            Assert.Equal(store.Values["host:friend.framenice.com"], store.Values["host:www.friendphoto.com"]);
        }

        [Fact]
        public async Task The_edge_is_told_which_modules_a_site_has_and_again_when_they_change()
        {
            await AddAsync("friend", "friend.framenice.com", "subdomain");
            Assert.Empty(Json(store.Values["host:friend.framenice.com"]).GetProperty("modules").EnumerateArray());

            var handler = new SetTenantModuleCommandHandler(platform, admin, TimeProvider.System, Edge());
            await handler.Handle(new SetTenantModuleCommand("friend", "booking", true), CancellationToken.None);
            await handler.Handle(new SetTenantModuleCommand("friend", "photos", true), CancellationToken.None);

            Assert.Equal(["booking", "photos"],
                Json(store.Values["host:friend.framenice.com"]).GetProperty("modules").EnumerateArray().Select(m => m.GetString()));

            await handler.Handle(new SetTenantModuleCommand("friend", "booking", false), CancellationToken.None);

            Assert.Equal(["photos"],
                Json(store.Values["host:friend.framenice.com"]).GetProperty("modules").EnumerateArray().Select(m => m.GetString()));
        }

        [Fact]
        public async Task A_site_with_no_address_can_still_publish()
        {
            await Edge().PointToVersionAsync("friend", 5, CancellationToken.None);
            Assert.Empty(store.Log);
        }

        [Fact]
        public async Task Adding_an_address_tells_the_edge_using_the_recorded_version()
        {
            await AddAsync("friend", "friend.framenice.com", "subdomain");

            var entry = Json(store.Values["host:friend.framenice.com"]);
            Assert.Equal(4, entry.GetProperty("contentVersion").GetInt32());
            Assert.Equal("friend.framenice.com", entry.GetProperty("primaryHost").GetString());
        }

        [Fact]
        public async Task A_site_that_has_not_published_is_in_the_map_with_no_version()
        {
            var tenant = await platform.Tenants.SingleAsync(t => t.Identifier == "friend");
            tenant.ContentVersion = null;
            await platform.SaveChangesAsync();

            await AddAsync("friend", "friend.framenice.com", "subdomain");

            Assert.Equal(JsonValueKind.Null, Json(store.Values["host:friend.framenice.com"]).GetProperty("contentVersion").ValueKind);
        }

        [Fact]
        public async Task A_site_with_its_own_code_is_never_in_the_map()
        {
            await AddAsync("aspensmitha", "www.aspensmitha.com", "custom", status: "active");
            await new UpdateTenantDomainCommandHandler(platform, admin, TimeProvider.System, Edge())
                .Handle(new UpdateTenantDomainCommand("aspensmitha", "www.aspensmitha.com", "active"), CancellationToken.None);
            await RemoveAsync("aspensmitha", "www.aspensmitha.com");

            Assert.Empty(store.Log);
        }

        [Fact]
        public async Task A_host_with_no_edge_store_still_records_addresses()
        {
            store.Configured = false;
            await AddAsync("friend", "friend.framenice.com", "subdomain");
            await RemoveAsync("friend", "friend.framenice.com");

            Assert.Empty(store.Log);
            Assert.Empty(await platform.TenantDomains.ToListAsync());
        }

        [Fact]
        public async Task Removing_an_address_takes_it_off_the_edge_before_the_record_goes()
        {
            await AddAsync("friend", "friend.framenice.com", "subdomain");

            store.FailDeletes = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => RemoveAsync("friend", "friend.framenice.com"));
            // The edge still has it, so the record must too: the removal can be tried again
            Assert.Single(await platform.TenantDomains.ToListAsync());

            store.FailDeletes = false;
            await RemoveAsync("friend", "friend.framenice.com");
            Assert.False(store.Values.ContainsKey("host:friend.framenice.com"));
            Assert.Empty(await platform.TenantDomains.ToListAsync());
        }

        [Fact]
        public async Task Suspending_a_site_reaches_the_edge()
        {
            await AddAsync("friend", "friend.framenice.com", "subdomain");

            await new SetTenantStatusCommandHandler(platform, Edge())
                .Handle(new SetTenantStatusCommand("friend", TenantStatusNames.Suspended), CancellationToken.None);

            Assert.Equal(TenantStatusNames.Suspended, Json(store.Values["host:friend.framenice.com"]).GetProperty("status").GetString());
        }

        // ---- Cloudflare KV

        [Fact]
        public async Task A_write_is_a_put_to_the_namespace_with_the_token()
        {
            var http = new RecordingHandler(HttpStatusCode.OK);
            await Kv(http).PutAsync("content:friend:5", """{"a":1}""", CancellationToken.None);

            var request = Assert.Single(http.Requests);
            Assert.Equal("PUT https://api.example.com/client/v4/accounts/acct/storage/kv/namespaces/ns/values/content%3Afriend%3A5", request.Line);
            Assert.Equal("Bearer secret-token", request.Authorization);
            Assert.Equal("""{"a":1}""", request.Body);
        }

        [Fact]
        public async Task Removing_a_key_that_is_not_there_succeeds()
        {
            var http = new RecordingHandler(HttpStatusCode.NotFound);
            await Kv(http).DeleteAsync("host:gone.example.com", CancellationToken.None);
            Assert.StartsWith("DELETE ", Assert.Single(http.Requests).Line);
        }

        [Fact]
        public async Task A_passing_fault_is_tried_again_and_a_refusal_is_not()
        {
            var flaky = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
            await Kv(flaky).PutAsync("host:a.example.com", "{}", CancellationToken.None);
            Assert.Equal(3, flaky.Requests.Count);

            var refused = new RecordingHandler(HttpStatusCode.Forbidden);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Kv(refused).PutAsync("host:a.example.com", "{}", CancellationToken.None));
            Assert.Single(refused.Requests);
            Assert.Contains("403", error.Message);
            Assert.DoesNotContain("secret-token", error.Message);

            var down = new RecordingHandler(HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Kv(down).PutAsync("host:a.example.com", "{}", CancellationToken.None));
            Assert.Equal(3, down.Requests.Count);
        }

        [Fact]
        public async Task Without_its_settings_the_store_says_what_is_missing_and_sends_nothing()
        {
            var http = new RecordingHandler(HttpStatusCode.OK);
            var store = Kv(http, new EdgeStoreSettings { AccountId = "acct", NamespaceId = "ns" });

            Assert.False(store.IsConfigured);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.PutAsync("host:a.example.com", "{}", CancellationToken.None));
            Assert.Contains("EdgeStore:AccountId, NamespaceId and ApiToken", error.Message);
            Assert.Empty(http.Requests);
        }

        [Fact]
        public async Task A_key_the_store_cannot_hold_is_refused_before_anything_is_sent()
        {
            var http = new RecordingHandler(HttpStatusCode.OK);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Kv(http).PutAsync("host:with space", "{}", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Kv(http).PutAsync(new string('k', 513), "{}", CancellationToken.None));
            Assert.Empty(http.Requests);
        }

        // ---- Helpers

        private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

        private Task<TenantDomainModel> AddAsync(string identifier, string host, string kind, string? status = null, bool makePrimary = false) =>
            new AddTenantDomainCommandHandler(platform, admin, TimeProvider.System, Edge())
                .Handle(new AddTenantDomainCommand(identifier, host, kind, status, makePrimary), CancellationToken.None);

        private Task RemoveAsync(string identifier, string host) =>
            new RemoveTenantDomainCommandHandler(platform, admin, TimeProvider.System, Edge())
                .Handle(new RemoveTenantDomainCommand(identifier, host), CancellationToken.None);

        private static CloudflareKvEdgeStore Kv(HttpMessageHandler handler, EdgeStoreSettings? settings = null) =>
            new(new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com/client/v4/") },
                Options.Create(settings ?? new EdgeStoreSettings { AccountId = "acct", NamespaceId = "ns", ApiToken = "secret-token" }),
                NullLogger<CloudflareKvEdgeStore>.Instance);

        private sealed class MemoryStore : IEdgeConfigStore
        {
            public Dictionary<string, string> Values { get; } = [];
            public List<string> Log { get; } = [];
            public bool Configured { get; set; } = true;
            public bool FailDeletes { get; set; }
            public bool IsConfigured => Configured;

            public Task PutAsync(string key, string json, CancellationToken cancellationToken)
            {
                Values[key] = json;
                Log.Add($"put {key}");
                return Task.CompletedTask;
            }

            public Task DeleteAsync(string key, CancellationToken cancellationToken)
            {
                if (FailDeletes)
                {
                    throw new InvalidOperationException("The edge store could not be reached.");
                }
                Values.Remove(key);
                Log.Add($"delete {key}");
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingHandler(params HttpStatusCode[] answers) : HttpMessageHandler
        {
            private readonly Queue<HttpStatusCode> answers = new(answers);
            public List<(string Line, string? Authorization, string? Body)> Requests { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(($"{request.Method} {request.RequestUri!.AbsoluteUri}", request.Headers.Authorization?.ToString(),
                    request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
                return new HttpResponseMessage(answers.Count > 1 ? answers.Dequeue() : answers.Peek());
            }
        }
    }
}
