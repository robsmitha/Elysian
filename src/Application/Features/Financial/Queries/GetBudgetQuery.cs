using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetBudgetQuery(int BudgetId) : IRequest<BudgetViewModel>;

    public class GetBudgetQueryHandler(IFinancialService financialService, IAccessTokenService accessTokenService,
            IBudgetService budgetService, ICategoryService categoryService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
            : IRequestHandler<GetBudgetQuery, BudgetViewModel>
    {
        public async Task<BudgetViewModel> Handle(GetBudgetQuery request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId;
            // Null when the budget doesn't exist or belongs to someone else
            var budget = await budgetService.GetBudgetAsync(userId, request.BudgetId)
                ?? throw new NotFoundException("Budget", request.BudgetId);
            var accessTokens = await accessTokenService.GetBudgetAccessTokensAsync(userId, budget.BudgetId);

            var expiredAccessItems = new List<ExpiredAccessItem>();
            var budgetAccessItems = new List<InstitutionAccessItemModel>();
            var allTransactions = new List<TransactionModel>();
            foreach (var accessToken in accessTokens)
            {
                // Every bank stays in the list, even one that needs attention, so editing the budget never drops it
                var accessItem = await AccessItemLoader.LoadAsync(financialService, accessToken);
                budgetAccessItems.Add(accessItem);

                if (accessItem.Status != AccessItemStatus.Healthy)
                {
                    if (accessItem.ExpiredAccessItem != null)
                    {
                        expiredAccessItems.Add(accessItem.ExpiredAccessItem);
                    }
                    continue;
                }

                try
                {
                    allTransactions.AddRange(await financialService.GetTransactionsAsync(accessToken.AccessToken, budget.StartDate, budget.EndDate));
                }
                catch (FinancialServiceException fex)
                {
                    AccessItemLoader.ApplyError(accessItem, fex.Error?.error_code, fex.Error?.error_type, fex.Error?.display_message);
                    if (accessItem.ExpiredAccessItem != null)
                    {
                        expiredAccessItems.Add(accessItem.ExpiredAccessItem);
                    }
                }
            }

            var excludedTransactions = await budgetService.GetExcludedTransactionsAsync(userId, budget.BudgetId);
            var budgetCategories = await budgetService.GetBudgetCategoriesAsync(userId, budget.BudgetId);
            var transactionCategories = await categoryService.GetTransactionCategoriesAsync(userId, budget.BudgetId);

            return new BudgetViewModel(budget.Name, budget.StartDate, budget.EndDate,
                budgetCategories, transactionCategories, allTransactions, budgetAccessItems,
                expiredAccessItems, excludedTransactions);
        }
    }
}
