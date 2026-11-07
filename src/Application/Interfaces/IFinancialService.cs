using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Interfaces
{
    public interface IFinancialService
    {
        Task<ItemModel> GetItemAsync(string accessToken);
        Task<List<AccountModel>> GetAccountsAsync(string accessToken);
        Task<string> RemoveItemAsync(string accessToken);
        Task<List<InstitutionModel>> GetInstitutionsAsync(int count, int offset);
        Task<InstitutionModel> GetInstitutionAsync(string institutionId);
        Task<List<TransactionModel>> GetTransactionsAsync(string accessToken, DateTime startDate, DateTime endDate);
        Task<string> RefreshTransactionsAsync(string accessToken);
        Task<InstitutionAccessTokenModel> SetAccessTokenAsync(string userId, string accessToken);
        Task<LinkTokenModel> CreateLinkTokenAsync(string userId, string? accessToken = null, bool accountSelection = false);
        Task<ItemPublicTokenExchangeModel> ItemPublicTokenExchangeAsync(string publicToken);

        /// <summary>One page of /transactions/sync after <paramref name="cursor"/> (from the start when null)</summary>
        Task<TransactionsSyncResponse> SyncTransactionsAsync(string accessToken, string? cursor);
        Task UpdateItemWebhookAsync(string accessToken, string webhookUrl);
        Task<WebhookVerificationKey> GetWebhookVerificationKeyAsync(string keyId);

        /// <summary>The webhook URL new and existing banks should use, or null when webhooks are off</summary>
        string? WebhookUrl { get; }
    }
}
