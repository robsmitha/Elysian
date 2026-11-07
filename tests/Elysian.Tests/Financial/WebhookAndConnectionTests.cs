using System.Security.Cryptography;
using System.Text;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Responses.Plaid;
using Elysian.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using static Elysian.Tests.Financial.PlaidTestKit;

namespace Elysian.Tests.Financial
{
    public sealed class PlaidWebhookTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly Mock<IFinancialService> plaid = new();
        private readonly FakeQueue queue = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));
        private readonly ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // A fresh key id per test, so the verifier's key cache never crosses tests
        private readonly string keyId = Guid.NewGuid().ToString("N");
        private readonly InstitutionAccessItem item;
        private readonly PlaidWebhookService service;

        public PlaidWebhookTests()
        {
            item = SeedBank(host, Protector(Settings())).Item;
            var p = signingKey.ExportParameters(false);
            plaid.Setup(x => x.GetWebhookVerificationKeyAsync(keyId)).ReturnsAsync(new WebhookVerificationKey
            {
                alg = "ES256", crv = "P-256", kid = keyId, kty = "EC", use = "sig",
                x = B64Url(p.Q.X!), y = B64Url(p.Q.Y!)
            });
            var verifier = new PlaidWebhookVerifier(plaid.Object, clock, NullLogger<PlaidWebhookVerifier>.Instance);
            service = new PlaidWebhookService(host.Context, verifier, queue, clock, NullLogger<PlaidWebhookService>.Instance);
        }

        private static string B64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private string Sign(byte[] body, DateTimeOffset? issuedAt = null)
        {
            var header = B64Url(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { alg = "ES256", kid = keyId, typ = "JWT" })));
            var claims = B64Url(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            {
                iat = (issuedAt ?? clock.Now).ToUnixTimeSeconds(),
                request_body_sha256 = PlaidWebhookVerifier.Sha256Hex(body)
            })));
            var signature = signingKey.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256);
            return $"{header}.{claims}.{B64Url(signature)}";
        }

        private static byte[] Body(object payload) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));

        [Fact]
        public async Task New_transactions_queue_a_sync()
        {
            var body = Body(new { webhook_type = "TRANSACTIONS", webhook_code = "SYNC_UPDATES_AVAILABLE", item_id = "item-1" });

            Assert.Equal(WebhookResult.Accepted, await service.HandleAsync(body, Sign(body)));
            Assert.Equal([(item.InstitutionAccessItemId, SyncTrigger.Webhook)], queue.Sent);
            Assert.NotNull((await host.Context.PlaidWebhookEvents.SingleAsync()).ProcessedAt);
        }

        [Fact]
        public async Task A_redelivered_webhook_is_handled_once()
        {
            var body = Body(new { webhook_type = "TRANSACTIONS", webhook_code = "SYNC_UPDATES_AVAILABLE", item_id = "item-1" });

            await service.HandleAsync(body, Sign(body));
            Assert.Equal(WebhookResult.Duplicate, await service.HandleAsync(body, Sign(body)));
            Assert.Single(queue.Sent);
        }

        [Fact]
        public async Task A_changed_body_is_rejected()
        {
            var body = Body(new { webhook_type = "TRANSACTIONS", webhook_code = "SYNC_UPDATES_AVAILABLE", item_id = "item-1" });
            var jwt = Sign(body);
            var tampered = Body(new { webhook_type = "TRANSACTIONS", webhook_code = "SYNC_UPDATES_AVAILABLE", item_id = "item-2" });

            Assert.Equal(WebhookResult.Rejected, await service.HandleAsync(tampered, jwt));
            Assert.Empty(queue.Sent);
            Assert.Empty(host.Context.PlaidWebhookEvents);
        }

        [Fact]
        public async Task An_old_or_unsigned_webhook_is_rejected()
        {
            var body = Body(new { webhook_type = "TRANSACTIONS", webhook_code = "SYNC_UPDATES_AVAILABLE", item_id = "item-1" });

            Assert.Equal(WebhookResult.Rejected, await service.HandleAsync(body, Sign(body, clock.Now.AddMinutes(-6))));
            Assert.Equal(WebhookResult.Rejected, await service.HandleAsync(body, null));
        }

        [Fact]
        public async Task A_signature_from_another_key_is_rejected()
        {
            var body = Body(new { webhook_type = "TRANSACTIONS", webhook_code = "SYNC_UPDATES_AVAILABLE", item_id = "item-1" });
            var parts = Sign(body).Split('.');
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var forged = B64Url(other.SignData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), HashAlgorithmName.SHA256));

            Assert.Equal(WebhookResult.Rejected, await service.HandleAsync(body, $"{parts[0]}.{parts[1]}.{forged}"));
        }

        [Theory]
        [InlineData("ERROR", "ITEM_LOGIN_REQUIRED", ItemStatus.LoginRequired)]
        [InlineData("ERROR", "ITEM_NOT_FOUND", ItemStatus.Disconnected)]
        [InlineData("PENDING_EXPIRATION", null, ItemStatus.PendingExpiration)]
        [InlineData("USER_PERMISSION_REVOKED", null, ItemStatus.Disconnected)]
        public async Task Item_webhooks_update_the_bank_status(string code, string? errorCode, ItemStatus expected)
        {
            var body = Body(new
            {
                webhook_type = "ITEM",
                webhook_code = code,
                item_id = "item-1",
                error = errorCode == null ? null : new { error_code = errorCode },
                consent_expiration_time = "2026-11-19T00:00:00Z"
            });

            await service.HandleAsync(body, Sign(body));

            Assert.Equal(expected, (await host.Context.InstitutionAccessItems.AsNoTracking().SingleAsync()).Status);
        }

        public void Dispose()
        {
            signingKey.Dispose();
            host.Dispose();
        }
    }

    public sealed class ConnectionServiceTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly Mock<IFinancialService> plaid = new();
        private readonly FakeQueue queue = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));
        private readonly AccessTokenProtector protector;
        private readonly ConnectionService service;

        public ConnectionServiceTests()
        {
            var settings = Settings();
            protector = Protector(settings);
            plaid.SetupGet(p => p.WebhookUrl).Returns(settings.WebhookUrl);
            plaid.Setup(p => p.GetInstitutionAsync(It.IsAny<string>())).ReturnsAsync(new InstitutionModel { institution_id = "ins_1", name = "Bank of Testing" });
            service = new ConnectionService(host.Context, plaid.Object, protector, TokenService(host, protector), queue,
                Options.Create(settings), clock, NullLogger<ConnectionService>.Instance);
        }

        private void SetupNewBank(string token = "access-new")
        {
            plaid.Setup(p => p.ItemPublicTokenExchangeAsync("public-1")).ReturnsAsync(new ItemPublicTokenExchangeModel { access_token = token, item_id = "item-new" });
            plaid.Setup(p => p.GetItemAsync(token)).ReturnsAsync(new ItemModel { ItemId = "item-new", InstitutionId = "ins_1" });
            plaid.Setup(p => p.GetAccountsAsync(token)).ReturnsAsync([
                new AccountModel { account_id = "a1", name = "Gold Card", mask = "1004", type = "credit", subtype = "credit card", balances = new BalancesModel { current = 2986.12m } },
                new AccountModel { account_id = "a2", name = "Savings", mask = "9035", type = "depository", subtype = "savings" }
            ]);
        }

        private static ExchangePublicTokenRequest Link(string mask = "1004") => new("public-1", "ins_1", [new LinkedAccountInput("Gold Card", mask, "credit card")]);

        [Fact]
        public async Task Linking_a_bank_stores_an_encrypted_token_and_its_accounts_and_queues_the_first_sync()
        {
            host.Context.Users.Add(TestHost.User(1, Me, "me@example.com"));
            await host.Context.SaveChangesAsync();
            SetupNewBank();

            var id = await service.ExchangePublicTokenAsync(Me, Link());

            var item = await host.Context.InstitutionAccessItems.Include(i => i.Accounts).SingleAsync();
            Assert.Equal(id, item.InstitutionAccessItemId);
            Assert.Null(item.AccessToken);
            Assert.Equal("access-new", protector.Unprotect(item.AccessTokenCiphertext!));
            Assert.Equal(2, item.Accounts.Count);
            Assert.Equal(AccountBudgetRole.Savings, item.Accounts.Single(a => a.PlaidAccountId == "a2").BudgetRole);
            Assert.Equal([(id, SyncTrigger.Initial)], queue.Sent);
        }

        [Fact]
        public async Task Linking_the_same_bank_twice_is_refused_before_calling_Plaid()
        {
            host.Context.Users.Add(TestHost.User(1, Me, "me@example.com"));
            await host.Context.SaveChangesAsync();
            SetupNewBank();
            await service.ExchangePublicTokenAsync(Me, Link());

            await Assert.ThrowsAsync<ConflictException>(() => service.ExchangePublicTokenAsync(Me, Link()));
            plaid.Verify(p => p.ItemPublicTokenExchangeAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task A_link_that_fails_to_save_is_removed_at_Plaid()
        {
            host.Context.Users.Add(TestHost.User(1, Me, "me@example.com"));
            await host.Context.SaveChangesAsync();
            SetupNewBank();
            plaid.Setup(p => p.GetAccountsAsync("access-new")).ThrowsAsync(new FinancialServiceException(new Error { error_code = "INTERNAL_SERVER_ERROR" }));

            await Assert.ThrowsAsync<FinancialServiceException>(() => service.ExchangePublicTokenAsync(Me, Link()));

            plaid.Verify(p => p.RemoveItemAsync("access-new"), Times.Once);
            Assert.Empty(host.Context.InstitutionAccessItems);
        }

        [Fact]
        public async Task Sync_now_is_limited_to_once_per_cooldown()
        {
            var (_, item) = SeedBank(host, protector);

            var first = await service.RequestSyncAsync(Me, item.InstitutionAccessItemId);
            var limited = await Assert.ThrowsAsync<TooManyRequestsException>(() => service.RequestSyncAsync(Me, item.InstitutionAccessItemId));

            Assert.Equal(clock.Now.AddMinutes(10), first.SyncAvailableAt);
            Assert.Equal(first.SyncAvailableAt, limited.RetryAt);
            Assert.Single(queue.Sent);

            clock.Now = clock.Now.AddMinutes(11);
            await service.RequestSyncAsync(Me, item.InstitutionAccessItemId);
            Assert.Equal(2, queue.Sent.Count);
        }

        [Fact]
        public async Task A_bank_that_needs_sign_in_has_to_be_reconnected_before_syncing()
        {
            var (_, item) = SeedBank(host, protector);
            item.Status = ItemStatus.LoginRequired;
            await host.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<ConflictException>(() => service.RequestSyncAsync(Me, item.InstitutionAccessItemId));

            await service.CompleteReconnectAsync(Me, item.InstitutionAccessItemId);
            Assert.Equal(ItemStatus.Healthy, (await host.Context.InstitutionAccessItems.AsNoTracking().SingleAsync()).Status);
            Assert.Equal((item.InstitutionAccessItemId, SyncTrigger.Reconnect), queue.Sent.Single());
        }

        [Fact]
        public async Task Someone_elses_bank_is_not_found()
        {
            var (_, item) = SeedBank(host, protector, userId: "someone-else", userKey: 2);

            await Assert.ThrowsAsync<NotFoundException>(() => service.RequestSyncAsync(Me, item.InstitutionAccessItemId));
            await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveAsync(Me, item.InstitutionAccessItemId, keepHistory: true));
            Assert.Empty((await service.GetConnectionsAsync(Me)).Connections);
        }

        [Fact]
        public async Task Removing_with_history_revokes_Plaid_access_and_keeps_past_transactions()
        {
            var (_, item) = SeedBank(host, protector);
            var account = new FinancialAccount { PlaidAccountId = "a1", Name = "Gold Card", Type = "credit", InstitutionAccessItemId = item.InstitutionAccessItemId };
            host.Context.FinancialAccounts.Add(account);
            host.Context.BankTransactions.Add(new BankTransaction { PlaidTransactionId = "t1", Name = "Publix", Amount = 10, FinancialAccount = account, Date = new DateOnly(2026, 11, 3), BudgetDate = new DateOnly(2026, 11, 3) });
            var budget = new Budget { Name = "November", StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30), UserId = Me };
            host.Context.Budgets.Add(budget);
            await host.Context.SaveChangesAsync();
            host.Context.BudgetAccessItems.Add(new BudgetAccessItem { BudgetId = budget.BudgetId, InstitutionAccessItemId = item.InstitutionAccessItemId });
            await host.Context.SaveChangesAsync();

            await service.RemoveAsync(Me, item.InstitutionAccessItemId, keepHistory: true);

            plaid.Verify(p => p.RemoveItemAsync("access-sandbox-1"), Times.Once);
            var saved = await host.Context.InstitutionAccessItems.AsNoTracking().SingleAsync();
            Assert.NotNull(saved.RemovedAt);
            Assert.Null(saved.AccessTokenCiphertext);
            Assert.Empty((await service.GetConnectionsAsync(Me)).Connections);

            // The budget still sees the bank (as disconnected) and its history
            var budgetItems = await service.GetBudgetAccessItemsAsync(Me, budget.BudgetId);
            Assert.Equal("disconnected", budgetItems.Single().Status);
            var transactions = await service.GetTransactionsAsync([item.InstitutionAccessItemId], budget.StartDate, budget.EndDate);
            Assert.Equal("t1", transactions.Single().transaction_id);
        }

        [Fact]
        public async Task Removing_a_bank_Plaid_already_forgot_still_works()
        {
            var (_, item) = SeedBank(host, protector);
            plaid.Setup(p => p.RemoveItemAsync(It.IsAny<string>())).ThrowsAsync(new FinancialServiceException(new Error { error_code = "ITEM_NOT_FOUND" }));

            await service.RemoveAsync(Me, item.InstitutionAccessItemId, keepHistory: false);

            Assert.Empty(host.Context.InstitutionAccessItems);
        }

        [Fact]
        public async Task The_accounts_page_reads_from_SQL_and_never_calls_Plaid()
        {
            var (_, item) = SeedBank(host, protector);
            item.LastSyncedAt = clock.Now.AddMinutes(-12);
            host.Context.FinancialAccounts.Add(new FinancialAccount { PlaidAccountId = "a1", Name = "Gold Card", Type = "credit", CurrentBalance = 2986.12m, InstitutionAccessItemId = item.InstitutionAccessItemId });
            await host.Context.SaveChangesAsync();

            var response = await service.GetConnectionsAsync(Me);

            var connection = response.Connections.Single();
            Assert.Equal("healthy", connection.Status);
            Assert.Equal(2986.12m, connection.Accounts.Single().CurrentBalance);
            plaid.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task A_bank_that_never_synced_gets_its_first_sync_queued_once()
        {
            var (_, item) = SeedBank(host, protector);

            var first = await service.GetConnectionsAsync(Me);
            await service.GetConnectionsAsync(Me);

            Assert.Equal("syncing", first.Connections.Single().Status);
            Assert.Equal((item.InstitutionAccessItemId, SyncTrigger.Initial), queue.Sent.Single());
        }

        public void Dispose() => host.Dispose();
    }
}
