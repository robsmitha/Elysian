using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Responses.Plaid;
using Elysian.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static Elysian.Tests.Financial.PlaidTestKit;

namespace Elysian.Tests.Financial
{
    public sealed class TransactionSyncServiceTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly Mock<IFinancialService> plaid = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));
        private readonly InstitutionAccessItem item;
        private readonly TransactionSyncService service;

        public TransactionSyncServiceTests()
        {
            var protector = Protector(Settings());
            item = SeedBank(host, protector).Item;
            plaid.SetupGet(p => p.WebhookUrl).Returns("https://robsmitha.com/api/PlaidWebhook?tenant=test");
            var classifier = new TransactionClassifier(host.Context, new CategoryService(host.Context, PlaidServiceTests.Mapper), clock);
            service = new TransactionSyncService(host.Context, plaid.Object, TokenService(host, protector), classifier, clock, NullLogger<TransactionSyncService>.Instance);
        }

        private void Returns(string? cursor, TransactionsSyncResponse page) =>
            plaid.Setup(p => p.SyncTransactionsAsync("access-sandbox-1", cursor)).ReturnsAsync(page);

        private List<BankTransaction> Stored() => host.Context.BankTransactions.AsNoTracking().OrderBy(t => t.PlaidTransactionId).ToList();

        [Fact]
        public async Task The_first_sync_stores_accounts_transactions_and_the_cursor()
        {
            Returns(null, Page("c1", hasMore: true, added: [Txn("t1", 86.42), Txn("t2", -32.50, name: "Amazon")]));
            Returns("c1", Page("c2", added: [Txn("t3", 41.25, pending: true)]));

            var outcome = await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Initial);

            Assert.True(outcome.Succeeded);
            Assert.Equal(3, outcome.Added);
            Assert.Equal(["t1", "t2", "t3"], Stored().Select(t => t.PlaidTransactionId));
            Assert.Equal(-32.50m, Stored().Single(t => t.PlaidTransactionId == "t2").Amount);
            Assert.Equal("FOOD_AND_DRINK_GROCERIES", Stored()[0].PfcDetailed);

            var account = await host.Context.FinancialAccounts.SingleAsync();
            Assert.Equal(1234.56m, account.CurrentBalance);
            Assert.Equal(AccountBudgetRole.Spending, account.BudgetRole);

            var saved = await host.Context.InstitutionAccessItems.AsNoTracking().SingleAsync();
            Assert.Equal("c2", saved.TransactionsCursor);
            Assert.Equal(clock.Now, saved.LastSyncedAt);
            Assert.Equal(3, (await host.Context.SyncRuns.SingleAsync()).Added);
        }

        [Fact]
        public async Task Running_the_same_changes_twice_keeps_one_row_each()
        {
            Returns(null, Page("c1", added: [Txn("t1", 10), Txn("t2", 20)]));
            await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Initial);

            // Plaid sends the same rows again as modified (amount changed on one)
            Returns("c1", Page("c2", modified: [Txn("t1", 10), Txn("t2", 25)]));
            var outcome = await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Webhook);

            Assert.Equal(0, outcome.Added);
            Assert.Equal(2, outcome.Modified);
            Assert.Equal(2, Stored().Count);
            Assert.Equal(25m, Stored().Single(t => t.PlaidTransactionId == "t2").Amount);
        }

        [Fact]
        public async Task A_posted_charge_keeps_the_category_and_exclusion_set_while_it_was_pending()
        {
            var budget = new Budget { Name = "November", StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30), UserId = Me };
            var groceries = new FinancialCategory { Name = "Groceries", UserId = Me };
            host.Context.AddRange(budget, groceries);
            await host.Context.SaveChangesAsync();

            Returns(null, Page("c1", added: [Txn("pending-1", 86.42, pending: true), Txn("pending-2", 600, pending: true)]));
            await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Initial);
            host.Context.TransactionCategories.Add(new TransactionCategory { TransactionId = "pending-1", BudgetId = budget.BudgetId, FinancialCategoryId = groceries.FinancialCategoryId });
            host.Context.BudgetExcludedTransactions.Add(new BudgetExcludedTransaction { TransactionId = "pending-2", BudgetId = budget.BudgetId });
            await host.Context.SaveChangesAsync();

            // Both post under new ids; Plaid removes the pending ones
            Returns("c1", Page("c2", added: [Txn("posted-1", 86.42, pendingId: "pending-1"), Txn("posted-2", 600, pendingId: "pending-2")],
                removed: ["pending-1", "pending-2"]));
            var outcome = await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Webhook);

            Assert.Equal(2, outcome.Removed);
            Assert.Equal("posted-1", (await host.Context.TransactionCategories.SingleAsync()).TransactionId);
            Assert.Equal("posted-2", (await host.Context.BudgetExcludedTransactions.SingleAsync()).TransactionId);
            Assert.All(Stored().Where(t => t.PlaidTransactionId.StartsWith("pending")), t => Assert.NotNull(t.RemovedAt));
            Assert.All(Stored().Where(t => t.PlaidTransactionId.StartsWith("posted")), t => Assert.Null(t.RemovedAt));
        }

        [Fact]
        public async Task Data_changing_mid_pagination_restarts_the_pass_from_the_first_cursor()
        {
            var calls = 0;
            plaid.Setup(p => p.SyncTransactionsAsync("access-sandbox-1", null)).ReturnsAsync(() =>
            {
                calls++;
                return Page("c1", hasMore: true, added: [Txn("t1", 10)]);
            });
            plaid.SetupSequence(p => p.SyncTransactionsAsync("access-sandbox-1", "c1"))
                .ThrowsAsync(new FinancialServiceException(new Error { error_type = "TRANSACTIONS_ERROR", error_code = "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" }))
                .ReturnsAsync(Page("c2", added: [Txn("t2", 20)]));

            var outcome = await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Initial);

            Assert.True(outcome.Succeeded);
            Assert.Equal(2, calls);
            Assert.Equal(["t1", "t2"], Stored().Select(t => t.PlaidTransactionId));
        }

        [Fact]
        public async Task A_sign_in_error_marks_the_bank_and_writes_nothing()
        {
            plaid.Setup(p => p.SyncTransactionsAsync(It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new FinancialServiceException(new Error { error_type = "ITEM_ERROR", error_code = "ITEM_LOGIN_REQUIRED" }));

            var outcome = await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Nightly);

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Retry);
            var saved = await host.Context.InstitutionAccessItems.AsNoTracking().SingleAsync();
            Assert.Equal(ItemStatus.LoginRequired, saved.Status);
            Assert.Null(saved.TransactionsCursor);
            Assert.Empty(Stored());
            Assert.Equal("ITEM_LOGIN_REQUIRED", (await host.Context.SyncRuns.SingleAsync()).ErrorCode);
        }

        [Fact]
        public async Task A_bank_outage_asks_the_queue_to_retry_and_leaves_the_status_alone()
        {
            plaid.Setup(p => p.SyncTransactionsAsync(It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new FinancialServiceException(new Error { error_type = "INSTITUTION_ERROR", error_code = "INSTITUTION_DOWN" }));

            var outcome = await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Webhook);

            Assert.True(outcome.Retry);
            Assert.Equal(ItemStatus.Healthy, (await host.Context.InstitutionAccessItems.AsNoTracking().SingleAsync()).Status);
        }

        [Fact]
        public async Task A_successful_sync_clears_an_earlier_sign_in_error()
        {
            item.Status = ItemStatus.LoginRequired;
            item.ErrorCode = "ITEM_LOGIN_REQUIRED";
            await host.Context.SaveChangesAsync();
            Returns(null, Page("c1"));

            await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Reconnect);

            var saved = await host.Context.InstitutionAccessItems.AsNoTracking().SingleAsync();
            Assert.Equal(ItemStatus.Healthy, saved.Status);
            Assert.Null(saved.ErrorCode);
        }

        [Fact]
        public async Task A_bank_linked_before_webhooks_gets_the_webhook_url_once()
        {
            item.WebhookUrl = null;
            await host.Context.SaveChangesAsync();
            Returns(null, Page("c1"));
            Returns("c1", Page("c2"));

            await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Initial);
            await service.SyncAsync(item.InstitutionAccessItemId, SyncTrigger.Nightly);

            plaid.Verify(p => p.UpdateItemWebhookAsync("access-sandbox-1", "https://robsmitha.com/api/PlaidWebhook?tenant=test"), Times.Once);
        }

        [Fact]
        public async Task The_nightly_job_picks_stale_banks_and_flags_consent_ending_soon()
        {
            item.LastSyncedAt = clock.Now.AddDays(-2);
            item.ConsentExpiresAt = clock.Now.AddDays(10);
            await host.Context.SaveChangesAsync();

            var due = await service.GetItemsDueForSyncAsync(TimeSpan.FromHours(20));

            Assert.Equal([item.InstitutionAccessItemId], due);
            Assert.Equal(ItemStatus.PendingExpiration, (await host.Context.InstitutionAccessItems.AsNoTracking().SingleAsync()).Status);
        }

        public void Dispose() => host.Dispose();
    }
}
