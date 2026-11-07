using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetBudgetQuery(int BudgetId) : IRequest<BudgetViewModel>;

    /// <summary>A budget with its banks and transactions, all read from SQL (stored by the sync).</summary>
    public class GetBudgetQueryHandler(IConnectionService connectionService, IBudgetService budgetService,
            ICategoryService categoryService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
            : IRequestHandler<GetBudgetQuery, BudgetViewModel>
    {
        public async Task<BudgetViewModel> Handle(GetBudgetQuery request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId!;
            // Null when the budget doesn't exist or belongs to someone else
            var budget = await budgetService.GetBudgetAsync(userId, request.BudgetId)
                ?? throw new NotFoundException("Budget", request.BudgetId);

            // Every bank stays in the list, even one that needs attention or was removed, so editing never drops it
            var budgetAccessItems = await connectionService.GetBudgetAccessItemsAsync(userId, budget.BudgetId);
            var expiredAccessItems = budgetAccessItems.Select(a => a.ExpiredAccessItem).OfType<ExpiredAccessItem>().ToList();
            var allTransactions = await connectionService.GetTransactionsAsync(
                budgetAccessItems.Select(a => a.InstitutionAccessItemId).ToList(), budget.StartDate, budget.EndDate);

            var excludedTransactions = await budgetService.GetExcludedTransactionsAsync(userId, budget.BudgetId);
            var budgetCategories = await budgetService.GetBudgetCategoriesAsync(userId, budget.BudgetId);
            var transactionCategories = await categoryService.GetTransactionCategoriesAsync(userId, budget.BudgetId);

            return new BudgetViewModel(budget.Name, budget.StartDate, budget.EndDate,
                budgetCategories, transactionCategories, allTransactions, budgetAccessItems,
                expiredAccessItems, excludedTransactions);
        }
    }
}
