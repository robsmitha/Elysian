using Elysian.Application.Features.Financial.Models;

namespace Elysian.Application.Interfaces
{
    public interface ICategoryService
    {
        Task<List<FinancialCategoryModel>> GetCategoriesAsync();
        Task<FinancialCategoryModel> GetCategoryAsync(int categoryId);
        Task<FinancialCategoryModel> GetCategoryByNameAsync(string categoryName);
        Task<FinancialCategoryModel> AddCategoryAsync(FinancialCategoryModel model);
        Task<FinancialCategoryModel> UpdateCategoryAsync(FinancialCategoryModel model);
        Task<List<TransactionCategoryModel>> GetTransactionCategoriesAsync(string userid, int budgetId);
        Task<TransactionCategoryModel> SetTransactionCategoryAsync(string userid, string transactionId, int categoryId, int budgetId);
    }
}
