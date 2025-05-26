using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetBudgetsQuery : IRequest<List<BudgetListItem>>;

    public class BudgetListItem(BudgetModel budget,
        List<BudgetCategoryModel> budgetCategories,
        List<TransactionModel> allTransactions,
        List<BudgetExcludedTransactionModel> excludedTransactions,
        bool expired)
    {
        public BudgetModel Budget { get; set; } = budget;
        public bool AccessExpired { get; set; } = expired;
        public decimal TransactionsTotal { get; set; } = (decimal)allTransactions
                .Where(t => !excludedTransactions.Any(e => e.TransactionId.Equals(t.transaction_id, StringComparison.OrdinalIgnoreCase)))
                .Sum(c => c.amount);
        public decimal EstimateTotal { get; set; } = budgetCategories.Sum(c => c.Estimate);
        public int TotalPercent => EstimateTotal > 0 ? (int)Math.Round((TransactionsTotal / EstimateTotal) * 100) : 0;
    }

    public class GetBudgetsQueryHandler(IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor,
        IAccessTokenService accessTokenService, IFinancialService financialService) : IRequestHandler<GetBudgetsQuery, List<BudgetListItem>>
    {
        public async Task<List<BudgetListItem>> Handle(GetBudgetsQuery request, CancellationToken cancellationToken)
        {
            var budgetList = new List<BudgetListItem>();

            var budgets = await budgetService.GetBudgetsAsync(claimsPrincipalAccessor.UserId);
            foreach (var budget in budgets)
            {
                var item = await GetBudgetItemAsync(budget);

                budgetList.Add(item);
            }
            return budgetList;
        }

        private async Task<BudgetListItem> GetBudgetItemAsync(BudgetModel budget)
        {
            var expired = false; 
            var accessTokens = await accessTokenService.GetBudgetAccessTokensAsync(claimsPrincipalAccessor.UserId, budget.BudgetId);
            var allTransactions = new List<TransactionModel>();
            foreach (var accessToken in accessTokens)
            {
                try
                {
                    var transactions = await financialService.GetTransactionsAsync(accessToken.AccessToken, budget.StartDate, budget.EndDate);
                    allTransactions.AddRange(transactions);
                }
                catch (FinancialServiceException fex)
                {
                    if (string.Equals(fex.Error?.error_code, PlaidErrorCodes.ITEM_LOGIN_REQUIRED,
                               StringComparison.InvariantCultureIgnoreCase))
                    {
                        expired = true;
                    }
                }
            }

            var excludedTransactions = await budgetService.GetExcludedTransactionsAsync(claimsPrincipalAccessor.UserId, budget.BudgetId);
            var budgetCategories = await budgetService.GetBudgetCategoriesAsync(claimsPrincipalAccessor.UserId, budget.BudgetId);

            return new BudgetListItem(budget, budgetCategories, allTransactions, excludedTransactions, expired);
        }
    }
}
