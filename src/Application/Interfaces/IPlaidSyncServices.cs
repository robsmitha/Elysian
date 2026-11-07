using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Features.Financial.Sync;
using Elysian.Domain.Constants;

namespace Elysian.Application.Interfaces
{
    /// <summary>Pulls a bank's changes from Plaid's /transactions/sync into SQL.</summary>
    public interface ITransactionSyncService
    {
        /// <summary>Syncs one bank for the current tenant. Never throws for Plaid errors; the outcome says what happened.</summary>
        Task<SyncOutcome> SyncAsync(int institutionAccessItemId, SyncTrigger trigger, CancellationToken cancellationToken = default);

        /// <summary>
        /// Banks the nightly job should sync: active ones not synced within <paramref name="olderThan"/>. Also flags banks
        /// whose consent ends within two weeks.
        /// </summary>
        Task<List<int>> GetItemsDueForSyncAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
    }

    /// <summary>Queues sync jobs (a Storage queue the Function App drains).</summary>
    public interface IPlaidSyncQueue
    {
        Task EnqueueAsync(int institutionAccessItemId, SyncTrigger trigger, CancellationToken cancellationToken = default);
    }

    /// <summary>Bank connections as the pages see them: read from SQL, never from Plaid on a page load.</summary>
    public interface IConnectionService
    {
        Task<ConnectionsResponse> GetConnectionsAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>The person's banks in the older page shape, read from SQL. Removed banks are left out.</summary>
        Task<List<InstitutionAccessItemModel>> GetAccessItemsAsync(string userId, int? institutionAccessItemId = null);

        /// <summary>A budget's banks, including removed ones (their history still counts), read from SQL.</summary>
        Task<List<InstitutionAccessItemModel>> GetBudgetAccessItemsAsync(string userId, int budgetId);

        /// <summary>Stored transactions for these banks whose budget date falls in the range (inclusive).</summary>
        Task<List<TransactionModel>> GetTransactionsAsync(IReadOnlyCollection<int> institutionAccessItemIds, DateTime start, DateTime end);

        /// <summary>Finishes linking a bank; refuses a duplicate. Returns the new connection's id.</summary>
        Task<int> ExchangePublicTokenAsync(string userId, ExchangePublicTokenRequest request, CancellationToken cancellationToken = default);

        /// <summary>Queues a sync for "Sync now", within the rate limit. Returns when it can be asked again.</summary>
        Task<SyncRequestResult> RequestSyncAsync(string userId, int institutionAccessItemId, CancellationToken cancellationToken = default);

        /// <summary>After Plaid Link update mode succeeds: mark the bank healthy again and sync it.</summary>
        Task CompleteReconnectAsync(string userId, int institutionAccessItemId, CancellationToken cancellationToken = default);

        Task RemoveAsync(string userId, int institutionAccessItemId, bool keepHistory, CancellationToken cancellationToken = default);

        Task UpdateAccountRoleAsync(string userId, int financialAccountId, AccountBudgetRole role, CancellationToken cancellationToken = default);
    }

    public enum WebhookResult
    {
        Accepted,
        /// <summary>Already received (Plaid retries deliveries); nothing done</summary>
        Duplicate,
        /// <summary>The signature, age or body hash didn't check out</summary>
        Rejected
    }

    /// <summary>Verifies, records and acts on Plaid webhooks.</summary>
    public interface IPlaidWebhookService
    {
        Task<WebhookResult> HandleAsync(byte[] body, string? verificationJwt, CancellationToken cancellationToken = default);
    }
}
