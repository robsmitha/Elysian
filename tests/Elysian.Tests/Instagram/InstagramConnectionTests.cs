using System.Net;
using System.Text;
using Azure;
using Elysian.Application.Exceptions;
using Elysian.Application.Features;
using Elysian.Application.Features.Instagram;
using Elysian.Application.Features.Instagram.Commands;
using Elysian.Application.Features.Instagram.Models;
using Elysian.Application.Features.Instagram.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Elysian.Tests.Instagram
{
    /// <summary>
    /// Connecting Instagram with the button: off to Instagram, back with a code, and disconnecting
    /// </summary>
    public sealed class InstagramConnectionTests : IDisposable
    {
        private static readonly string Tenant = TestHost.Tenant.Identifier!;

        private readonly TestHost host = new();
        private readonly Mock<IInstagramOAuthClient> oauth = new();
        private readonly Mock<IInstagramService> instagram = new();
        private readonly Mock<IPhotoStorage> storage = new();
        private readonly Mock<ISender> mediator = new();
        private readonly MemoryCache cache = new(new MemoryCacheOptions());
        private readonly Clock clock = new();
        private readonly DbInstagramTokenStore tokens;

        public InstagramConnectionTests()
        {
            tokens = new DbInstagramTokenStore(host.Context);
            oauth.SetupGet(o => o.CanAuthorize).Returns(true);
            oauth.Setup(o => o.AuthorizationUrl(It.IsAny<string>())).Returns((string state) => $"https://instagram.example/auth?state={state}");
            instagram.Setup(i => i.GetAccountAsync("long-lived", It.IsAny<CancellationToken>())).ReturnsAsync(new InstagramAccount("user-1", "jo.photos"));
        }

        public void Dispose()
        {
            cache.Dispose();
            host.Dispose();
        }

        [Fact]
        public async Task Starting_gives_the_Instagram_address_with_a_state_that_names_the_site_and_is_remembered_for_the_person()
        {
            var result = await Start();

            var attempt = await host.Context.OAuthStates.SingleAsync();
            Assert.Equal(OAuthProviders.Instagram, attempt.OAuthProvider);
            Assert.Equal("actor-swa-id", attempt.UserId);
            Assert.StartsWith("test.", attempt.State);
            Assert.Equal(5 + 64, attempt.State.Length);
            Assert.EndsWith($"state={attempt.State}", result.Url);
        }

        [Fact]
        public async Task Starting_again_replaces_the_earlier_attempt_and_leaves_other_providers_alone()
        {
            host.Context.OAuthStates.Add(new OAuthState { OAuthProvider = OAuthProviders.Instagram, UserId = "someone-else", State = "test.old", CreatedAt = clock.Now.UtcDateTime.AddHours(-1) });
            host.Context.OAuthStates.Add(new OAuthState { OAuthProvider = OAuthProviders.Google, UserId = "actor-swa-id", State = "test.google", CreatedAt = clock.Now.UtcDateTime });
            await host.Context.SaveChangesAsync();
            await Start();

            await Start();

            var states = await host.Context.OAuthStates.ToListAsync();
            Assert.Single(states, s => s.OAuthProvider == OAuthProviders.Instagram);
            Assert.Single(states, s => s.OAuthProvider == OAuthProviders.Google);
        }

        [Fact]
        public async Task Starting_says_so_when_the_platform_has_no_Meta_app()
        {
            oauth.SetupGet(o => o.CanAuthorize).Returns(false);

            await Assert.ThrowsAsync<CustomValidationException>(Start);
            Assert.Empty(host.Context.OAuthStates);
        }

        [Fact]
        public async Task Completing_stores_the_long_lived_token_with_its_expiry_syncs_the_posts_and_uses_up_the_attempt()
        {
            var state = await StartedState();
            Grants(new InstagramAuthorization("long-lived", TimeSpan.FromDays(60), [InstagramPermissions.Basic]));

            var result = await Complete("code", state);

            Assert.True(result.Connected);
            Assert.Equal("jo.photos", result.Username);
            Assert.Equal(clock.Now.AddDays(60), result.ExpiresUtc);
            var token = await tokens.GetAsync();
            Assert.Equal("long-lived", token!.AccessToken);
            Assert.Equal("user-1", token.UserId);
            Assert.Equal(clock.Now, token.LastRefreshedUtc);
            Assert.Equal(clock.Now.AddDays(60), token.ExpiresUtc);
            Assert.Empty(host.Context.OAuthStates);
            mediator.Verify(m => m.Send(It.IsAny<SyncInstagramPostsCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Completing_replaces_an_earlier_connection()
        {
            await tokens.SaveAsync(new InstagramToken("pasted", "user-0", clock.Now.AddDays(-3), null));
            var state = await StartedState();
            Grants(new InstagramAuthorization("long-lived", TimeSpan.FromDays(60), []));

            await Complete("code", state);

            var row = await host.Context.OAuthTokens.SingleAsync();
            Assert.Equal("user-1", row.UserId);
            Assert.Equal("long-lived", row.AccessToken);
        }

        [Fact]
        public async Task A_state_works_once()
        {
            var state = await StartedState();
            Grants(new InstagramAuthorization("long-lived", TimeSpan.FromDays(60), []));
            await Complete("code", state);

            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));
            oauth.Verify(o => o.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_state_nobody_started_or_that_someone_else_started_is_refused_without_asking_Instagram()
        {
            var state = await StartedState();

            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", "test.made-up"));

            host.ClaimsPrincipalAccessor.Principal = TestHost.Principal("another-person", "other@example.com");
            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));

            oauth.Verify(o => o.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Null(await tokens.GetAsync());
        }

        [Fact]
        public async Task A_Google_state_is_not_accepted_for_Instagram()
        {
            host.Context.OAuthStates.Add(new OAuthState { OAuthProvider = OAuthProviders.Google, UserId = "actor-swa-id", State = "test.google", CreatedAt = clock.Now.UtcDateTime });
            await host.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", "test.google"));

            oauth.Verify(o => o.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_state_left_too_long_is_refused()
        {
            var state = await StartedState();
            clock.Now += OAuthConnectionState.Lifetime + TimeSpan.FromSeconds(1);

            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));

            oauth.Verify(o => o.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(host.Context.OAuthStates);
        }

        [Fact]
        public async Task A_grant_without_the_posts_permission_keeps_the_old_connection()
        {
            await tokens.SaveAsync(new InstagramToken("old", "user-1", clock.Now, null));
            var state = await StartedState();
            Grants(new InstagramAuthorization("long-lived", TimeSpan.FromDays(60), ["instagram_business_manage_messages"]));

            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));

            Assert.Equal("old", (await tokens.GetAsync())!.AccessToken);
            mediator.Verify(m => m.Send(It.IsAny<SyncInstagramPostsCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_code_Instagram_rejects_or_an_Instagram_outage_is_reported_plainly_and_stores_nothing()
        {
            var state = await StartedState();
            oauth.Setup(o => o.ExchangeCodeAsync("code", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InstagramApiException("Instagram returned 400: Matching code was not found or was already used", 400));
            var rejected = await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));
            Assert.Equal(CompleteInstagramConnectionCommandHandler.TryAgain, rejected.Errors["Instagram"][0]);

            state = await StartedState();
            oauth.Setup(o => o.ExchangeCodeAsync("code", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InstagramApiException("Instagram could not be reached (HttpRequestException)."));
            var outage = await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));
            Assert.Contains("try again", outage.Errors["Instagram"][0]);

            Assert.Null(await tokens.GetAsync());
        }

        [Fact]
        public void The_code_never_appears_when_the_command_is_logged()
        {
            Assert.DoesNotContain("secret-code", new CompleteInstagramConnectionCommand("secret-code", "test.state").ToString());
        }

        [Fact]
        public async Task Disconnecting_forgets_the_token_the_posts_and_their_thumbnails()
        {
            await tokens.SaveAsync(new InstagramToken("long-lived", "user-1", clock.Now, clock.Now.AddDays(60)));
            await AddPostAsync();
            cache.Set(GetInstagramPostsQueryHandler.CacheKey(Tenant), "cached");

            var result = await Disconnect();

            Assert.False(result.Connected);
            Assert.Equal(0, result.PostCount);
            Assert.Empty(host.Context.OAuthTokens);
            Assert.Empty(host.Context.InstagramPosts);
            Assert.False(cache.TryGetValue(GetInstagramPostsQueryHandler.CacheKey(Tenant), out _));
            storage.Verify(s => s.DeleteVariantsAsync($"{Tenant}/instagram/", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Disconnecting_leaves_other_connections_alone()
        {
            await tokens.SaveAsync(new InstagramToken("long-lived", "user-1", clock.Now, null));
            host.Context.OAuthTokens.Add(new OAuthToken { OAuthProvider = OAuthProviders.Google, UserId = "jo@example.com", AccessToken = "a", TokenType = "bearer", Scope = "s", CreatedAt = clock.Now.UtcDateTime });
            await host.Context.SaveChangesAsync();

            await Disconnect();

            Assert.Equal(OAuthProviders.Google, (await host.Context.OAuthTokens.SingleAsync()).OAuthProvider);
        }

        [Fact]
        public async Task Disconnecting_still_finishes_when_the_thumbnails_cannot_be_deleted()
        {
            await tokens.SaveAsync(new InstagramToken("long-lived", "user-1", clock.Now, null));
            await AddPostAsync();
            storage.Setup(s => s.DeleteVariantsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(500, "storage down"));

            await Disconnect();

            Assert.Empty(host.Context.OAuthTokens);
            Assert.Empty(host.Context.InstagramPosts);
        }

        [Fact]
        public void The_Instagram_address_asks_only_to_read_posts_and_returns_to_the_admin_app()
        {
            var client = Client(new StubHandler());

            var url = new Uri(client.AuthorizationUrl("test.abc"));
            var query = url.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

            Assert.True(client.CanAuthorize);
            Assert.Equal("www.instagram.com", url.Host);
            Assert.Equal("/oauth/authorize", url.AbsolutePath);
            Assert.Equal("app-id", query["client_id"]);
            Assert.Equal("https://manage.example.com/integrations/instagram/callback", query["redirect_uri"]);
            Assert.Equal("code", query["response_type"]);
            Assert.Equal("instagram_business_basic", query["scope"]);
            Assert.Equal("test.abc", query["state"]);
            Assert.DoesNotContain("app-secret", url.ToString());
        }

        [Fact]
        public void Connecting_is_unavailable_until_the_app_and_return_address_are_configured()
        {
            var client = new InstagramOAuthClient(new HttpClient(new StubHandler()), Options.Create(new InstagramSettings { AppId = "app-id", AppSecret = "app-secret" }));

            Assert.False(client.CanAuthorize);
        }

        [Theory]
        [InlineData("{\"data\":[{\"access_token\":\"short\",\"user_id\":\"1020\",\"permissions\":\"instagram_business_basic,instagram_business_manage_comments\"}]}")]
        [InlineData("{\"access_token\":\"short\",\"user_id\":1020,\"permissions\":[\"instagram_business_basic\",\"instagram_business_manage_comments\"]}")]
        public async Task A_code_is_exchanged_for_a_short_token_and_that_for_a_long_lived_one(string codeResponse)
        {
            var handler = new StubHandler();
            handler.Respond(HttpStatusCode.OK, codeResponse);
            handler.Respond(HttpStatusCode.OK, "{\"access_token\":\"long-lived\",\"token_type\":\"bearer\",\"expires_in\":5183944}");

            var granted = await Client(handler).ExchangeCodeAsync("the-code#_");

            Assert.Equal("long-lived", granted.AccessToken);
            Assert.Equal(TimeSpan.FromSeconds(5183944), granted.ExpiresIn);
            Assert.Equal(["instagram_business_basic", "instagram_business_manage_comments"], granted.Permissions);

            var (first, form) = handler.Requests[0];
            Assert.Equal(HttpMethod.Post, first.Method);
            Assert.Equal("https://api.instagram.com/oauth/access_token", first.RequestUri!.ToString());
            Assert.Contains("client_id=app-id", form);
            Assert.Contains("client_secret=app-secret", form);
            Assert.Contains("grant_type=authorization_code", form);
            Assert.Contains("redirect_uri=https%3A%2F%2Fmanage.example.com%2Fintegrations%2Finstagram%2Fcallback", form);
            Assert.EndsWith("code=the-code", form);

            var second = handler.Requests[1].Request;
            Assert.Equal(HttpMethod.Get, second.Method);
            Assert.Equal("https://graph.instagram.com/access_token?grant_type=ig_exchange_token&client_secret=app-secret&access_token=short",
                second.RequestUri!.ToString());
        }

        [Fact]
        public async Task A_rejected_code_is_reported_with_Instagrams_reason_and_never_the_secret()
        {
            var handler = new StubHandler();
            handler.Respond(HttpStatusCode.BadRequest, "{\"error_type\":\"OAuthException\",\"code\":400,\"error_message\":\"Matching code was not found or was already used\"}");

            var error = await Assert.ThrowsAsync<InstagramApiException>(() => Client(handler).ExchangeCodeAsync("used"));

            Assert.Equal(400, error.StatusCode);
            Assert.Contains("Matching code was not found", error.Message);
            Assert.DoesNotContain("app-secret", error.ToString());
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task A_failed_long_lived_exchange_or_an_unreadable_answer_is_an_error()
        {
            var handler = new StubHandler();
            handler.Respond(HttpStatusCode.OK, "{\"access_token\":\"short\"}");
            handler.Respond(HttpStatusCode.InternalServerError, "<html>oops</html>");
            var failed = await Assert.ThrowsAsync<InstagramApiException>(() => Client(handler).ExchangeCodeAsync("code"));
            Assert.Equal(500, failed.StatusCode);
            Assert.DoesNotContain("app-secret", failed.ToString());

            handler = new StubHandler();
            handler.Respond(HttpStatusCode.OK, "{\"data\":[]}");
            await Assert.ThrowsAsync<InstagramApiException>(() => Client(handler).ExchangeCodeAsync("code"));
        }

        private static InstagramOAuthClient Client(StubHandler handler) =>
            new(new HttpClient(handler), Options.Create(new InstagramSettings
            {
                AppId = "app-id",
                AppSecret = "app-secret",
                RedirectUri = "https://manage.example.com/integrations/instagram/callback",
            }));

        private void Grants(InstagramAuthorization authorization) =>
            oauth.Setup(o => o.ExchangeCodeAsync("code", It.IsAny<CancellationToken>())).ReturnsAsync(authorization);

        private async Task AddPostAsync()
        {
            host.Context.InstagramPosts.Add(new InstagramPost
            {
                MediaId = "17970841014043744",
                Permalink = "https://instagram.com/p/a",
                MediaType = "IMAGE",
                Width = 1080,
                Height = 1080,
                VariantWidths = [320, 640],
                SyncedAt = clock.Now,
            });
            await host.Context.SaveChangesAsync();
        }

        private async Task<string> StartedState()
        {
            await Start();
            return (await host.Context.OAuthStates.SingleAsync(s => s.OAuthProvider == OAuthProviders.Instagram)).State;
        }

        private Task<InstagramAuthorizationModel> Start() =>
            new StartInstagramConnectionCommandHandler(host.Context, oauth.Object, host.ClaimsPrincipalAccessor, host.TenantAccessor, clock)
                .Handle(new StartInstagramConnectionCommand(), default);

        private Task<InstagramConnectionModel> Complete(string code, string state) =>
            new CompleteInstagramConnectionCommandHandler(host.Context, oauth.Object, instagram.Object, tokens, host.ClaimsPrincipalAccessor,
                mediator.Object, clock, NullLogger<CompleteInstagramConnectionCommandHandler>.Instance)
                .Handle(new CompleteInstagramConnectionCommand(code, state), default);

        private Task<InstagramConnectionModel> Disconnect() =>
            new DisconnectInstagramCommandHandler(host.Context, tokens, storage.Object, cache, host.TenantAccessor,
                NullLogger<DisconnectInstagramCommandHandler>.Instance)
                .Handle(new DisconnectInstagramCommand(), default);

        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => Now;
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Queue<(HttpStatusCode Status, string Body)> responses = new();

            public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

            public void Respond(HttpStatusCode status, string body) => responses.Enqueue((status, body));

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add((request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
                var (status, body) = responses.Dequeue();
                return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
        }
    }
}
