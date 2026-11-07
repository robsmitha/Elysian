using System.Net;
using System.Security.Cryptography;
using System.Text;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Tests.Financial
{
    public class AccessTokenProtectorTests
    {
        [Fact]
        public void A_token_round_trips_and_is_not_stored_in_plaintext()
        {
            var protector = PlaidTestKit.Protector(PlaidTestKit.Settings());

            var stored = protector.Protect("access-sandbox-abc");

            Assert.StartsWith("v1:k1:", stored);
            Assert.DoesNotContain("access-sandbox-abc", stored);
            Assert.Equal("access-sandbox-abc", protector.Unprotect(stored));
        }

        [Fact]
        public void The_same_token_encrypts_differently_each_time()
        {
            var protector = PlaidTestKit.Protector(PlaidTestKit.Settings());

            Assert.NotEqual(protector.Protect("same"), protector.Protect("same"));
        }

        [Fact]
        public void A_tampered_token_is_refused()
        {
            var protector = PlaidTestKit.Protector(PlaidTestKit.Settings());
            var stored = protector.Protect("access-sandbox-abc");
            var bytes = Convert.FromBase64String(stored.Split(':')[2]);
            bytes[^1] ^= 0x01;

            Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect($"v1:k1:{Convert.ToBase64String(bytes)}"));
        }

        [Fact]
        public void A_token_from_a_rotated_out_key_still_reads()
        {
            var oldKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var stored = PlaidTestKit.Protector(PlaidTestKit.Settings(oldKey)).Protect("access-sandbox-abc");

            var rotated = PlaidTestKit.Settings();
            rotated.TokenEncryptionKeyId = "k2";
            rotated.PreviousTokenEncryptionKeys["k1"] = oldKey;

            Assert.Equal("access-sandbox-abc", PlaidTestKit.Protector(rotated).Unprotect(stored));
        }

        [Fact]
        public void Without_a_key_nothing_can_be_protected()
        {
            var settings = PlaidTestKit.Settings();
            settings.TokenEncryptionKey = null;
            var protector = PlaidTestKit.Protector(settings);

            Assert.False(protector.CanProtect);
            Assert.Throws<InvalidOperationException>(() => protector.Protect("x"));
        }

        [Fact]
        public async Task A_plaintext_token_from_before_encryption_is_encrypted_when_read()
        {
            using var host = new TestHost();
            var protector = PlaidTestKit.Protector(PlaidTestKit.Settings());
            var user = TestHost.User(1, PlaidTestKit.Me, "me@example.com");
            host.Context.Users.Add(user);
            var legacy = new InstitutionAccessItem
            {
                AccessToken = "access-legacy-plain",
                ItemId = "item-old",
                InstitutionId = "ins_1",
                AccessUsers = [new InstitutionAccessItemUser { UserId = 1 }]
            };
            host.Context.InstitutionAccessItems.Add(legacy);
            await host.Context.SaveChangesAsync();

            var token = await PlaidTestKit.TokenService(host, protector).GetAccessTokenAsync(PlaidTestKit.Me, legacy.InstitutionAccessItemId);

            Assert.Equal("access-legacy-plain", token!.AccessToken);
            var row = await host.Context.InstitutionAccessItems.SingleAsync();
            Assert.Null(row.AccessToken);
            Assert.Equal("access-legacy-plain", protector.Unprotect(row.AccessTokenCiphertext!));
        }
    }

    public class PlaidRetryHandlerTests
    {
        // Each call gets a fresh response (the handler disposes the ones it retries); the last one repeats
        private sealed class Scripted(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
        {
            private int next;
            public int Calls => next;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(responses[Math.Min(next++, responses.Length - 1)]());
        }

        private static Func<HttpResponseMessage> Status(HttpStatusCode code, string body = "{}") =>
            () => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static async Task<(HttpResponseMessage Response, int Calls, List<TimeSpan> Waits)> Send(string path, params Func<HttpResponseMessage>[] responses)
        {
            var waits = new List<TimeSpan>();
            var inner = new Scripted(responses);
            var handler = new PlaidRetryHandler((d, _) => { waits.Add(d); return Task.CompletedTask; }) { InnerHandler = inner };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://sandbox.plaid.com/") };
            var response = await client.PostAsync(path, new StringContent("{\"a\":1}", Encoding.UTF8, "application/json"));
            return (response, inner.Calls, waits);
        }

        [Fact]
        public async Task A_rate_limit_is_retried_with_backoff()
        {
            var (response, calls, waits) = await Send("transactions/sync", Status(HttpStatusCode.TooManyRequests), Status(HttpStatusCode.OK));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, calls);
            Assert.InRange(waits.Single(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.3));
        }

        [Fact]
        public async Task A_bank_outage_reported_as_400_is_retried()
        {
            var down = """{"error_type":"INSTITUTION_ERROR","error_code":"INSTITUTION_DOWN"}""";
            var (_, calls, _) = await Send("accounts/get", Status(HttpStatusCode.BadRequest, down), Status(HttpStatusCode.OK));

            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task A_login_error_is_not_retried()
        {
            var login = """{"error_type":"ITEM_ERROR","error_code":"ITEM_LOGIN_REQUIRED"}""";
            var (response, calls, _) = await Send("accounts/get", Status(HttpStatusCode.BadRequest, login));

            Assert.Equal(1, calls);
            Assert.Contains("ITEM_LOGIN_REQUIRED", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task It_gives_up_after_three_retries()
        {
            var (response, calls, waits) = await Send("transactions/sync", Status(HttpStatusCode.InternalServerError));

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(4, calls);
            Assert.Equal(3, waits.Count);
        }

        [Fact]
        public async Task The_one_time_public_token_exchange_is_never_retried()
        {
            var (_, calls, _) = await Send("item/public_token/exchange", Status(HttpStatusCode.InternalServerError), Status(HttpStatusCode.OK));

            Assert.Equal(1, calls);
        }
    }
}
