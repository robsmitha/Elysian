using Elysian.Application.Features.Financial.Models;

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
        Task<LinkTokenModel> CreateLinkTokenAsync(string userId, string accessToken = null);
        Task<ItemPublicTokenExchangeModel> ItemPublicTokenExchangeAsync(string publicToken);
    }
}
