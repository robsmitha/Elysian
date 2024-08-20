using Elysian.Application.Features.Financial.Models;

namespace Elysian.Application.Interfaces
{
    public interface IBudgetService
    {
        Task<List<BudgetModel>> GetBudgetsAsync(string userId);
        Task<BudgetModel> GetBudgetAsync(string userId, int budgetId);
        Task<BudgetModel> AddBudgetAsync(string userId, BudgetModel model);
        Task<BudgetModel> UpdateBudgetAsync(string userId, BudgetModel model);
        Task<List<BudgetCategoryModel>> GetBudgetCategoriesAsync(string userId, int budgetId);
        Task<BudgetCategoryModel> UpdateBudgetCategoryEstimateAsync(string userId, int budgetId, int categoryId, decimal estimate);
        Task AddBudgetCategoryAsync(string userId, int budgetId, int categoryId, decimal? estimate = null);
        Task<BudgetExcludedTransactionModel> SetExcludedTransactionAsync(string userId, int budgetId, string transactionId);
        Task<bool> RestoreExcludedTransactionAsync(string userId, int budgetId, string transactionId);
        Task<List<BudgetExcludedTransactionModel>> GetExcludedTransactionsAsync(string userId, int budgetId);
    }
}
