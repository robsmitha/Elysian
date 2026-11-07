using Elysian.Application.Features.Financial.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>A person's own categories. The default set is created the first time they're needed.</summary>
    public interface ICategoryService
    {
        Task<List<FinancialCategoryModel>> GetCategoriesAsync(string userId, bool includeArchived = false);
        Task<FinancialCategoryModel?> GetCategoryAsync(string userId, int categoryId);
        Task<FinancialCategoryModel?> GetCategoryByNameAsync(string userId, string categoryName);
        Task<FinancialCategoryModel> AddCategoryAsync(string userId, FinancialCategoryModel model);
        Task<FinancialCategoryModel> UpdateCategoryAsync(string userId, FinancialCategoryModel model);

        /// <summary>Hides a category; with <paramref name="mergeIntoCategoryId"/>, its transactions, rules and budget lines move there first.</summary>
        Task ArchiveCategoryAsync(string userId, int categoryId, int? mergeIntoCategoryId);

        /// <summary>Gives a new person the default categories, and keys existing ones whose names match a default.</summary>
        Task EnsureDefaultsAsync(string userId);
    }
}
