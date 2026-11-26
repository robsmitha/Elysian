using System.Text;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.GoogleCalendar;
using Elysian.Application.Features.GoogleCalendar.Commands;
using Elysian.Application.Features.GoogleCalendar.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Elysian.Tests.Booking
{
    /// <summary>
    /// Connecting a calendar with the button: off to Google, back with a code, and disconnecting
    /// </summary>
    public sealed class GoogleCalendarConnectionTests : IDisposable
    {
        private const string Events = GoogleCalendarScopes.Events;

        private readonly TestHost host = new();
        private readonly Mock<IGoogleOAuthClient> oauth = new();
        private readonly Mock<IGoogleCalendarService> calendar = new();
        private readonly Clock clock = new();
        private readonly DbGoogleTokenStore tokens;

        public GoogleCalendarConnectionTests()
        {
            tokens = new DbGoogleTokenStore(host.Context);
            oauth.SetupGet(o => o.CanAuthorize).Returns(true);
            oauth.Setup(o => o.AuthorizationUrl(It.IsAny<string>(), It.IsAny<string?>()))
                .Returns((string state, string? hint) => $"https://google.example/auth?state={state}&hint={hint}");
            calendar.Setup(c => c.VerifyAccessAsync("access", It.IsAny<CancellationToken>())).ReturnsAsync("Jo's calendar");
        }

        public void Dispose() => host.Dispose();

        [Fact]
        public async Task Starting_gives_the_Google_address_with_a_state_that_names_the_site_and_is_remembered_for_the_person()
        {
            var result = await Start();

            var attempt = await host.Context.OAuthStates.SingleAsync();
            Assert.Equal(OAuthProviders.Google, attempt.OAuthProvider);
            Assert.Equal("actor-swa-id", attempt.UserId);
            Assert.StartsWith("test.", attempt.State);
            Assert.Equal(5 + 64, attempt.State.Length);
            Assert.Contains($"state={attempt.State}", result.Url);
        }

        [Fact]
        public async Task Starting_again_replaces_the_earlier_attempt_and_clears_abandoned_ones()
        {
            host.Context.OAuthStates.Add(new OAuthState { OAuthProvider = OAuthProviders.Google, UserId = "someone-else", State = "test.old", CreatedAt = clock.Now.UtcDateTime.AddHours(-1) });
            host.Context.OAuthStates.Add(new OAuthState { OAuthProvider = OAuthProviders.GitHub, UserId = "actor-swa-id", State = "github", CreatedAt = clock.Now.UtcDateTime.AddHours(-1) });
            await host.Context.SaveChangesAsync();
            await Start();

            await Start();

            var states = await host.Context.OAuthStates.ToListAsync();
            Assert.Single(states, s => s.OAuthProvider == OAuthProviders.Google);
            Assert.Single(states, s => s.OAuthProvider == OAuthProviders.GitHub);
        }

        [Fact]
        public async Task Starting_offers_the_account_connected_before()
        {
            await tokens.SaveAsync(new GoogleToken("old", "jo@example.com", Events, null, null, clock.Now, null));

            var result = await Start();

            Assert.EndsWith("hint=jo@example.com", result.Url);
        }

        [Fact]
        public async Task Starting_says_so_when_the_platform_has_no_Google_client()
        {
            oauth.SetupGet(o => o.CanAuthorize).Returns(false);

            await Assert.ThrowsAsync<CustomValidationException>(Start);
            Assert.Empty(host.Context.OAuthStates);
        }

        [Fact]
        public async Task Completing_stores_the_tokens_under_the_Google_account_and_uses_up_the_attempt()
        {
            var state = await StartedState();
            Grants(new GoogleAuthorization("access", TimeSpan.FromHours(1), "refresh", $"openid {Events}", "jo@example.com"));

            var result = await Complete("code", state);

            Assert.True(result.Connected);
            Assert.Equal("jo@example.com", result.AccountId);
            var token = await tokens.GetAsync();
            Assert.Equal("refresh", token!.RefreshToken);
            Assert.Equal("jo@example.com", token.AccountId);
            Assert.Equal(clock.Now, token.LastValidatedUtc);
            Assert.Empty(host.Context.OAuthStates);
        }

        [Fact]
        public async Task Completing_names_the_connection_after_the_calendar_when_Google_shares_no_address()
        {
            var state = await StartedState();
            Grants(new GoogleAuthorization("access", TimeSpan.FromHours(1), "refresh", Events, null));

            var result = await Complete("code", state);

            Assert.Equal("Jo's calendar", result.AccountId);
        }

        [Fact]
        public async Task Completing_replaces_an_earlier_connection()
        {
            await tokens.SaveAsync(new GoogleToken("old", "old@example.com", GoogleCalendarScopes.FullCalendar, null, null, clock.Now, null));
            var state = await StartedState();
            Grants(new GoogleAuthorization("access", TimeSpan.FromHours(1), "refresh", Events, "jo@example.com"));

            await Complete("code", state);

            var row = await host.Context.OAuthTokens.SingleAsync();
            Assert.Equal("jo@example.com", row.UserId);
            Assert.Equal("refresh", row.RefreshToken);
        }

        [Fact]
        public async Task A_state_works_once()
        {
            var state = await StartedState();
            Grants(new GoogleAuthorization("access", TimeSpan.FromHours(1), "refresh", Events, "jo@example.com"));
            await Complete("code", state);

            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));
            oauth.Verify(o => o.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_state_nobody_started_or_that_someone_else_started_is_refused_without_asking_Google()
        {
            var state = await StartedState();

            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", "test.made-up"));

            host.ClaimsPrincipalAccessor.Principal = TestHost.Principal("another-person", "other@example.com");
            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));

            oauth.Verify(o => o.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Null(await tokens.GetAsync());
        }

        [Fact]
        public async Task A_state_left_too_long_is_refused()
        {
            var state = await StartedState();
            clock.Now += GoogleCalendarConnectionState.Lifetime + TimeSpan.FromSeconds(1);

            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));

            oauth.Verify(o => o.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(host.Context.OAuthStates);
        }

        [Fact]
        public async Task Unticking_the_calendar_permission_keeps_the_old_connection_and_withdraws_the_new_grant()
        {
            await tokens.SaveAsync(new GoogleToken("old", "jo@example.com", Events, null, null, clock.Now, null));
            var state = await StartedState();
            Grants(new GoogleAuthorization("access", TimeSpan.FromHours(1), "refresh", "openid https://www.googleapis.com/auth/userinfo.email", "jo@example.com"));

            var error = await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));

            Assert.Contains("ticked", error.Errors["GoogleCalendar"][0]);
            Assert.Equal("old", (await tokens.GetAsync())!.RefreshToken);
            oauth.Verify(o => o.RevokeAsync("refresh", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_grant_without_lasting_access_or_a_calendar_that_cannot_be_read_stores_nothing()
        {
            var state = await StartedState();
            Grants(new GoogleAuthorization("access", TimeSpan.FromHours(1), null, Events, "jo@example.com"));
            await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));

            state = await StartedState();
            Grants(new GoogleAuthorization("access", TimeSpan.FromHours(1), "refresh", Events, "jo@example.com"));
            calendar.Setup(c => c.VerifyAccessAsync("access", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CalendarAuthorizationException(CalendarAuthorizationFailure.Forbidden, "Google refused access"));
            var error = await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));

            Assert.Equal("Google refused access", error.Errors["GoogleCalendar"][0]);
            Assert.Null(await tokens.GetAsync());
        }

        [Fact]
        public async Task A_code_Google_rejects_or_a_Google_outage_is_reported_plainly()
        {
            var state = await StartedState();
            oauth.Setup(o => o.ExchangeCodeAsync("code", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CalendarAuthorizationException(CalendarAuthorizationFailure.Revoked, "invalid_grant"));
            var rejected = await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));
            Assert.Equal(CompleteGoogleCalendarConnectionCommandHandler.TryAgain, rejected.Errors["GoogleCalendar"][0]);

            state = await StartedState();
            oauth.Setup(o => o.ExchangeCodeAsync("code", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("timed out"));
            var outage = await Assert.ThrowsAsync<CustomValidationException>(() => Complete("code", state));
            Assert.Contains("try again", outage.Errors["GoogleCalendar"][0]);
        }

        [Fact]
        public void The_code_never_appears_when_the_command_is_logged()
        {
            Assert.DoesNotContain("secret-code", new CompleteGoogleCalendarConnectionCommand("secret-code", "test.state").ToString());
        }

        [Fact]
        public async Task Disconnecting_withdraws_the_grant_at_Google_and_forgets_the_tokens()
        {
            await tokens.SaveAsync(new GoogleToken("refresh", "jo@example.com", Events, "access", clock.Now, clock.Now, null));

            var result = await Disconnect();

            Assert.False(result.Connected);
            Assert.Empty(host.Context.OAuthTokens);
            oauth.Verify(o => o.RevokeAsync("refresh", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Disconnecting_still_forgets_the_tokens_when_Google_will_not_confirm()
        {
            await tokens.SaveAsync(new GoogleToken("refresh", "jo@example.com", Events, null, null, clock.Now, null));
            oauth.Setup(o => o.RevokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("invalid_token"));

            await Disconnect();

            Assert.Empty(host.Context.OAuthTokens);
        }

        [Fact]
        public async Task Disconnecting_with_nothing_connected_does_nothing()
        {
            var result = await Disconnect();

            Assert.False(result.Connected);
            oauth.Verify(o => o.RevokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(GoogleCalendarScopes.Events, true)]
        [InlineData("openid " + GoogleCalendarScopes.FullCalendar, true)]
        [InlineData("openid https://www.googleapis.com/auth/calendar.events.readonly", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Booking_needs_the_events_scope_or_the_full_one(string? granted, bool allowed)
        {
            Assert.Equal(allowed, GoogleCalendarScopes.AllowsBooking(granted));
        }

        [Fact]
        public void The_Google_address_asks_for_lasting_access_to_events_only_and_returns_to_the_admin_app()
        {
            var client = new GoogleOAuthClient(Options.Create(new GoogleCalendarSettings
            {
                ClientId = "client-id",
                ClientSecret = "client-secret",
                RedirectUri = "https://manage.example.com/integrations/google/callback",
            }));

            var url = new Uri(client.AuthorizationUrl("test.abc", "jo@example.com"));
            var query = url.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

            Assert.True(client.CanAuthorize);
            Assert.Equal("accounts.google.com", url.Host);
            Assert.Equal("client-id", query["client_id"]);
            Assert.Equal("https://manage.example.com/integrations/google/callback", query["redirect_uri"]);
            Assert.Equal("code", query["response_type"]);
            Assert.Equal($"openid email {GoogleCalendarScopes.Events}", query["scope"]);
            Assert.Equal("offline", query["access_type"]);
            Assert.Equal("consent select_account", query["prompt"]);
            Assert.Equal("test.abc", query["state"]);
            Assert.Equal("jo@example.com", query["login_hint"]);
            Assert.DoesNotContain("client-secret", url.ToString());
        }

        [Fact]
        public void Connecting_is_unavailable_until_the_return_address_is_configured()
        {
            var client = new GoogleOAuthClient(Options.Create(new GoogleCalendarSettings { ClientId = "client-id", ClientSecret = "client-secret" }));

            Assert.False(client.CanAuthorize);
        }

        [Fact]
        public void Reads_the_address_from_an_id_token_and_gives_none_for_anything_else()
        {
            static string Segment(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            Assert.Equal("jo@example.com", GoogleOAuthClient.EmailFrom($"{Segment("{}")}.{Segment("{\"sub\":\"1\",\"email\":\"jo@example.com\"}")}.sig"));
            Assert.Null(GoogleOAuthClient.EmailFrom($"{Segment("{}")}.{Segment("{\"sub\":\"1\"}")}.sig"));
            Assert.Null(GoogleOAuthClient.EmailFrom("not-a-token"));
            Assert.Null(GoogleOAuthClient.EmailFrom("a.!!!.c"));
            Assert.Null(GoogleOAuthClient.EmailFrom(null));
        }

        private void Grants(GoogleAuthorization authorization) =>
            oauth.Setup(o => o.ExchangeCodeAsync("code", It.IsAny<CancellationToken>())).ReturnsAsync(authorization);

        private async Task<string> StartedState()
        {
            await Start();
            return (await host.Context.OAuthStates.SingleAsync(s => s.OAuthProvider == OAuthProviders.Google)).State;
        }

        private Task<GoogleCalendarAuthorizationModel> Start() =>
            new StartGoogleCalendarConnectionCommandHandler(host.Context, oauth.Object, tokens, host.ClaimsPrincipalAccessor, host.TenantAccessor, clock)
                .Handle(new StartGoogleCalendarConnectionCommand(), default);

        private Task<GoogleCalendarConnectionModel> Complete(string code, string state) =>
            new CompleteGoogleCalendarConnectionCommandHandler(host.Context, oauth.Object, calendar.Object, tokens, host.ClaimsPrincipalAccessor, clock,
                NullLogger<CompleteGoogleCalendarConnectionCommandHandler>.Instance)
                .Handle(new CompleteGoogleCalendarConnectionCommand(code, state), default);

        private Task<GoogleCalendarConnectionModel> Disconnect() =>
            new DisconnectGoogleCalendarCommandHandler(oauth.Object, tokens, NullLogger<DisconnectGoogleCalendarCommandHandler>.Instance)
                .Handle(new DisconnectGoogleCalendarCommand(), default);

        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
