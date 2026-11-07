using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Constants;

namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Decides each stored transaction's category and treatment (docs/budget-plan.md §4.4): the person's own choice, then
    /// their rules, then transfer pairs, then Plaid's category, then refunds, then a default.
    /// </summary>
    public interface ITransactionClassifier
    {
        /// <summary>
        /// Classifies the person's transactions that are new or changed. With <paramref name="reclassifyAll"/>, every one
        /// not set by hand (after a rule changes). Returns how many were looked at.
        /// </summary>
        Task<int> ClassifyAsync(string userId, bool reclassifyAll = false, CancellationToken cancellationToken = default);
    }

    /// <summary>Searching and editing stored transactions, and the rules that place them.</summary>
    public interface ITransactionService
    {
        Task<TransactionSearchResult> SearchAsync(string userId, TransactionSearch search, CancellationToken cancellationToken = default);
        Task<TransactionModel> UpdateAsync(string userId, TransactionUpdate update, CancellationToken cancellationToken = default);
        Task<int> BulkUpdateAsync(string userId, IReadOnlyCollection<long> ids, int? categoryId, Treatment? treatment, CancellationToken cancellationToken = default);

        /// <summary>The older budget pages name transactions by Plaid's id.</summary>
        Task SetCategoryByPlaidIdAsync(string userId, string plaidTransactionId, int categoryId, CancellationToken cancellationToken = default);
        Task SetTreatmentByPlaidIdAsync(string userId, string plaidTransactionId, Treatment treatment, CancellationToken cancellationToken = default);

        Task<List<CategoryRuleModel>> GetRulesAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Saves a rule; with <paramref name="applyToPast"/>, re-places every transaction not set by hand. Returns how many it matches.</summary>
        Task<RuleSaveResult> SaveRuleAsync(string userId, CategoryRuleModel rule, bool applyToPast, CancellationToken cancellationToken = default);
        Task DeleteRuleAsync(string userId, int ruleId, CancellationToken cancellationToken = default);
        /// <summary>How many of the person's transactions (not set by hand) a rule would change.</summary>
        Task<int> PreviewRuleAsync(string userId, CategoryRuleModel rule, CancellationToken cancellationToken = default);
    }
}
