using Elysian.Application.Features.Financial.Models;

namespace Elysian.Application.Interfaces
{
    public interface IAccessTokenService
    {
        Task<List<InstitutionAccessTokenModel>> GetAccessTokensAsync(string userId);
        Task<InstitutionAccessTokenModel> GetAccessTokenAsync(string userId, string itemId);
        Task<InstitutionAccessTokenModel> GetAccessTokenAsync(string userId, int institutionAccessItemId);
        Task<InstitutionAccessTokenModel> SetAccessTokenAsync(string userId, string accessToken, string itemId, string institutionId);
        Task<List<InstitutionAccessTokenModel>> GetBudgetAccessTokensAsync(string userId, int budgetId);
    }
}
