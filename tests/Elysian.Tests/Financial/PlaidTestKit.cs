using System.Security.Cryptography;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Responses.Plaid;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.Financial
{
    /// <summary>Shared builders for the Plaid sync tests.</summary>
    public static class PlaidTestKit
    {
        public const string Me = "me-swa-id";

        public static PlaidSettings Settings(string? key = null) => new()
        {
            BaseUrl = "https://sandbox.plaid.com/",
            ClientId = "client",
            Secret = "secret",
            TokenEncryptionKey = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            TokenEncryptionKeyId = "k1",
            WebhookUrl = "https://robsmitha.com/api/PlaidWebhook?tenant=test"
        };

        public static AccessTokenProtector Protector(PlaidSettings settings) => new(Options.Create(settings));

        public static AccessTokenService TokenService(TestHost host, IAccessTokenProtector protector) =>
            new(host.Context, PlaidServiceTests.Mapper, protector, NullLogger<AccessTokenService>.Instance);

        /// <summary>A user plus one linked bank with an encrypted token.</summary>
        public static (User User, InstitutionAccessItem Item) SeedBank(TestHost host, IAccessTokenProtector protector, string token = "access-sandbox-1",
            string userId = Me, int userKey = 1, string itemId = "item-1", string institutionId = "ins_1")
        {
            var user = host.Context.Users.FirstOrDefault(u => u.ExternalUserId == userId);
            if (user == null)
            {
                user = TestHost.User(userKey, userId, $"{userId}@example.com");
                host.Context.Users.Add(user);
            }
            var item = new InstitutionAccessItem
            {
                AccessTokenCiphertext = protector.Protect(token),
                ItemId = itemId,
                InstitutionId = institutionId,
                InstitutionName = "Bank of Testing",
                WebhookUrl = "https://robsmitha.com/api/PlaidWebhook?tenant=test",
                AccessUsers = [new InstitutionAccessItemUser { UserId = user.UserId }]
            };
            host.Context.InstitutionAccessItems.Add(item);
            host.Context.SaveChanges();
            return (user, item);
        }

        public static Account Card(string id = "acct-1", string name = "Gold Card", double balance = 1234.56) => new()
        {
            account_id = id,
            name = name,
            mask = "1004",
            type = "credit",
            subtype = "credit card",
            balances = new Balances { current = balance, iso_currency_code = "USD" }
        };

        public static TransactionResponse.Transaction Txn(string id, double amount, string date = "2026-11-03", bool pending = false,
            string? pendingId = null, string account = "acct-1", string name = "Publix") => new()
        {
            transaction_id = id,
            account_id = account,
            amount = amount,
            date = date,
            authorized_date = date,
            name = name,
            merchant_name = name,
            pending = pending,
            pending_transaction_id = pendingId,
            personal_finance_category = new TransactionResponse.PersonalFinanceCategory { primary = "FOOD_AND_DRINK", detailed = "FOOD_AND_DRINK_GROCERIES", confidence_level = "HIGH" }
        };

        public static TransactionsSyncResponse Page(string next, bool hasMore = false, IEnumerable<TransactionResponse.Transaction>? added = null,
            IEnumerable<TransactionResponse.Transaction>? modified = null, IEnumerable<string>? removed = null, params Account[] accounts) => new()
        {
            next_cursor = next,
            has_more = hasMore,
            added = added?.ToList() ?? [],
            modified = modified?.ToList() ?? [],
            removed = (removed ?? []).Select(id => new TransactionsSyncResponse.RemovedTransaction { transaction_id = id }).ToList(),
            accounts = accounts.Length > 0 ? [.. accounts] : [Card()]
        };

        /// <summary>Records queued syncs instead of sending them.</summary>
        public sealed class FakeQueue : IPlaidSyncQueue
        {
            public List<(int ItemId, SyncTrigger Trigger)> Sent { get; } = [];

            public Task EnqueueAsync(int institutionAccessItemId, SyncTrigger trigger, CancellationToken cancellationToken = default)
            {
                Sent.Add((institutionAccessItemId, trigger));
                return Task.CompletedTask;
            }
        }

        /// <summary>A clock the test can move.</summary>
        public sealed class FakeClock(DateTimeOffset start) : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = start;
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
