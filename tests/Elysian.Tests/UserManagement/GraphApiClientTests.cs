using System.Net;
using System.Text;
using Azure.Core;
using Elysian.Application.Exceptions;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.UserManagement
{
    public class GraphApiClientTests
    {
        private readonly FakeHandler handler = new();

        private GraphApiClient Client(int maxRetries = 3) => new(
            new HttpClient(handler) { BaseAddress = new Uri(GraphApiClient.BaseAddress) },
            new FakeCredential(),
            new GraphRetryOptions { MaxRetries = maxRetries, BaseDelay = TimeSpan.Zero },
            NullLogger<GraphApiClient>.Instance);

        [Fact]
        public async Task Retries_429_and_503_honoring_retry_after_then_succeeds()
        {
            handler.Enqueue(HttpStatusCode.TooManyRequests, """{"error":{"code":"TooManyRequests","message":"slow down"}}""", retryAfterSeconds: 0);
            handler.Enqueue(HttpStatusCode.ServiceUnavailable, "", retryAfterSeconds: 0);
            handler.Enqueue(HttpStatusCode.OK, """{"id":"oid-1","displayName":"Pat"}""");

            var user = await Client().SendAsync<Application.Interfaces.EntraUser>(HttpMethod.Get, "users/oid-1");

            Assert.Equal("oid-1", user!.Id);
            Assert.Equal(3, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal("Bearer test-token", r.Authorization));
        }

        [Fact]
        public async Task Throws_throttled_after_the_last_retry()
        {
            for (var i = 0; i < 3; i++)
            {
                handler.Enqueue(HttpStatusCode.TooManyRequests, """{"error":{"code":"TooManyRequests","message":"slow down"}}""", retryAfterSeconds: 0);
            }

            var ex = await Assert.ThrowsAsync<GraphThrottledException>(() => Client(maxRetries: 2).SendAsync(HttpMethod.Get, "users"));

            Assert.Equal(429, ex.StatusCode);
            Assert.Equal(3, handler.Requests.Count);
        }

        [Fact]
        public async Task Gives_up_without_waiting_when_retry_after_is_too_long()
        {
            handler.Enqueue(HttpStatusCode.TooManyRequests, "", retryAfterSeconds: 3600);

            var ex = await Assert.ThrowsAsync<GraphThrottledException>(() => Client().SendAsync(HttpMethod.Get, "users"));

            Assert.Equal(TimeSpan.FromHours(1), ex.RetryAfter);
            Assert.Single(handler.Requests);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, typeof(GraphNotFoundException))]
        [InlineData(HttpStatusCode.Conflict, typeof(GraphConflictException))]
        [InlineData(HttpStatusCode.Forbidden, typeof(GraphForbiddenException))]
        [InlineData(HttpStatusCode.InternalServerError, typeof(GraphApiException))]
        public async Task Maps_errors_to_typed_exceptions(HttpStatusCode status, Type expected)
        {
            handler.Enqueue(status, """{"error":{"code":"Some_Code","message":"details"}}""");

            var ex = await Assert.ThrowsAnyAsync<GraphApiException>(() => Client().SendAsync(HttpMethod.Get, "users/x?$filter=mail eq 'pat@example.com'"));

            Assert.IsType(expected, ex);
            Assert.Equal("Some_Code", ex.ErrorCode);
            // Query strings can hold emails; they stay out of messages
            Assert.DoesNotContain("pat@example.com", ex.Message);
        }

        [Fact]
        public async Task Existing_assignment_is_returned_as_success()
        {
            handler.Enqueue(HttpStatusCode.BadRequest,
                """{"error":{"code":"Request_BadRequest","message":"Permission being assigned already exists on the object"}}""");
            handler.Enqueue(HttpStatusCode.OK,
                """{"value":[{"id":"existing","principalId":"oid-1","principalType":"User","resourceId":"sp-1","appRoleId":"00000000-0000-0000-0000-000000000000"}]}""");

            var directory = new EntraDirectoryService(Client(), Options.Create(new EntraSettings
            {
                EnterpriseAppServicePrincipalId = "sp-1",
                AppRoleId = Guid.Empty.ToString(),
                AppUrl = "https://app.example.com"
            }));

            var assignment = await directory.AssignAsync("oid-1");

            Assert.Equal("existing", assignment.Id);
            Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
            Assert.Contains("servicePrincipals/sp-1/appRoleAssignedTo", handler.Requests[0].Uri);
            Assert.Contains("users/oid-1/appRoleAssignments", handler.Requests[1].Uri);
        }

        [Fact]
        public async Task Invitation_never_asks_microsoft_to_send_its_email()
        {
            handler.Enqueue(HttpStatusCode.Created,
                """{"inviteRedeemUrl":"https://redeem/secret","status":"PendingAcceptance","invitedUser":{"id":"oid-1"}}""");

            var directory = new EntraDirectoryService(Client(), Options.Create(new EntraSettings { AppUrl = "https://app.example.com" }));
            var invitation = await directory.InviteGuestAsync("pat@example.com", "Pat");

            Assert.Equal("oid-1", invitation.UserId);
            Assert.Contains("\"sendInvitationMessage\":false", handler.Requests[0].Body);
            Assert.Contains("\"inviteRedirectUrl\":\"https://app.example.com\"", handler.Requests[0].Body);
            Assert.DoesNotContain("secret", invitation.ToString());
        }

        [Fact]
        public async Task Batch_returns_bodies_and_null_for_missing_users()
        {
            handler.Enqueue(HttpStatusCode.OK, """
                {"responses":[
                    {"id":"0","status":200,"body":{"id":"oid-1","userType":"Guest","externalUserState":"Accepted"}},
                    {"id":"1","status":404,"body":{"error":{"code":"Request_ResourceNotFound","message":"gone"}}}
                ]}
                """);

            var directory = new EntraDirectoryService(Client(), Options.Create(new EntraSettings()));
            var users = await directory.GetUsersAsync(["oid-1", "oid-2"]);

            Assert.Single(users);
            Assert.Equal("Accepted", users["oid-1"].ExternalUserState);
            Assert.Single(handler.Requests);
            Assert.EndsWith("$batch", handler.Requests[0].Uri);
        }

        private class FakeCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                new("test-token", DateTimeOffset.UtcNow.AddHours(1));

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }

        private class FakeHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> responses = new();

            public List<(HttpMethod Method, string Uri, string? Authorization, string Body)> Requests { get; } = [];

            public void Enqueue(HttpStatusCode status, string body, int? retryAfterSeconds = null)
            {
                responses.Enqueue(() =>
                {
                    var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                    if (retryAfterSeconds is int seconds)
                    {
                        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
                    }
                    return response;
                });
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
                return responses.Dequeue()();
            }
        }
    }
}
