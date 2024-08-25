using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    public class GetBudgetQuery(int budgetId) : IRequest<BudgetViewModel>
    {
        private int BudgetId { get; set; } = budgetId;

        public class Handler(IFinancialService financialService, IAccessTokenService accessTokenService,
            IBudgetService budgetService, ICategoryService categoryService, IClaimsPrincipalAccessor claimsPrincipalAccessor) 
            : IRequestHandler<GetBudgetQuery, BudgetViewModel>
        {
            public async Task<BudgetViewModel> Handle(GetBudgetQuery request, CancellationToken cancellationToken)
            {
                var budget = await budgetService.GetBudgetAsync(claimsPrincipalAccessor.UserId, request.BudgetId);
                var accessTokens = await accessTokenService.GetBudgetAccessTokensAsync(claimsPrincipalAccessor.UserId, budget.BudgetId);

                var expiredAccessTokens = new List<ExpiredAccessItem>();
                var budgetAccessItems = new List<InstitutionAccessItemModel>();
                var allTransactions = new List<TransactionModel>();
                foreach (var accessToken in accessTokens)
                {
                    var institution = await financialService.GetInstitutionAsync(accessToken.InstitutionId);
                    try
                    {
                        var accounts = await financialService.GetAccountsAsync(accessToken.AccessToken);
                        var item = await financialService.GetItemAsync(accessToken.AccessToken);
                        
                        budgetAccessItems.Add(new InstitutionAccessItemModel
                        {
                            Accounts = accounts,
                            Institution = institution,
                            Item = item,
                            InstitutionAccessItemId = accessToken.InstitutionAccessItemId
                        });

                        var transactions = await financialService.GetTransactionsAsync(accessToken.AccessToken, budget.StartDate, budget.EndDate);
                        
                        allTransactions.AddRange(transactions);
                    }
                    catch (FinancialServiceException fex)
                    {
                        if (string.Equals(fex.Error?.error_code, PlaidErrorCodes.ITEM_LOGIN_REQUIRED,
                                   StringComparison.InvariantCultureIgnoreCase))
                        {
                            expiredAccessTokens.Add(new ExpiredAccessItem(accessToken.AccessToken, fex.Error.error_message, institution));
                        }
                    }
                }

                var excludedTransactions = await budgetService.GetExcludedTransactionsAsync(claimsPrincipalAccessor.UserId, budget.BudgetId);
                var budgetCategories = await budgetService.GetBudgetCategoriesAsync(claimsPrincipalAccessor.UserId, request.BudgetId);
                var transactionCategories = await categoryService.GetTransactionCategoriesAsync(claimsPrincipalAccessor.UserId, request.BudgetId);

                return new BudgetViewModel(budget.Name, budget.StartDate, budget.EndDate, 
                    budgetCategories, transactionCategories, allTransactions, budgetAccessItems, 
                    expiredAccessTokens, excludedTransactions);
            }
        }

    }
}
