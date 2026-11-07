using Elysian.Application.Features.Financial.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Reads and stores bank connections' access tokens. Tokens are encrypted at rest and decrypted only here.
    /// Methods taking a userId only return banks linked by that person; removed banks are left out.
    /// </summary>
    public interface IAccessTokenService
    {
        Task<List<InstitutionAccessTokenModel>> GetAccessTokensAsync(string userId);
        Task<InstitutionAccessTokenModel?> GetAccessTokenAsync(string userId, string itemId);
        Task<InstitutionAccessTokenModel?> GetAccessTokenAsync(string userId, int institutionAccessItemId);
        Task<InstitutionAccessTokenModel> SetAccessTokenAsync(string userId, string accessToken, string itemId, string institutionId);
        Task<List<InstitutionAccessTokenModel>> GetBudgetAccessTokensAsync(string userId, int budgetId);

        /// <summary>For background syncs, which act for no signed-in person. Null when the bank is gone or removed.</summary>
        Task<InstitutionAccessTokenModel?> GetAccessTokenForSyncAsync(int institutionAccessItemId);
    }
}
