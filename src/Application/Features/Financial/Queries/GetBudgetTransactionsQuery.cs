using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetBudgetTransactionsQuery(int BudgetId) : IRequest<GetBudgetTransactionsQueryResponse>;

    public record GetBudgetTransactionsQueryResponse(List<TransactionModel> Transactions, List<ExpiredAccessItem> ExpiredAccessItems);

    public class GetBudgetTransactionsQueryHandler(IFinancialService financialService, IAccessTokenService accessTokenService,
        IBudgetService budgetService, ICategoryService categoryService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<GetBudgetTransactionsQuery, GetBudgetTransactionsQueryResponse>
    {
        public async Task<GetBudgetTransactionsQueryResponse> Handle(GetBudgetTransactionsQuery request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId;
            var allTransactions = new List<TransactionModel>();
            var expiredAccessItems = new List<ExpiredAccessItem>();
            var budget = await budgetService.GetBudgetAsync(userId, request.BudgetId)
                ?? throw new NotFoundException("Budget", request.BudgetId);
            var accessTokens = await accessTokenService.GetBudgetAccessTokensAsync(userId, budget.BudgetId);
            foreach (var accessToken in accessTokens)
            {
                try
                {
                    var transactions = await financialService.GetTransactionsAsync(accessToken.AccessToken, budget.StartDate, budget.EndDate);
                    allTransactions.AddRange(transactions);
                }
                catch (FinancialServiceException fex) when (AccessItemLoader.IsLoginRequired(fex))
                {
                    var institution = await AccessItemLoader.GetInstitutionAsync(financialService, accessToken.InstitutionId);
                    expiredAccessItems.Add(AccessItemLoader.ExpiredFor(fex, accessToken.InstitutionAccessItemId, institution)!);
                }
                catch (FinancialServiceException)
                {
                    // A bank that's temporarily down shouldn't hide the others
                }
            }
            var transactionCategories = await categoryService.GetTransactionCategoriesAsync(userId, request.BudgetId);
            var transactionCategoryData = from t in allTransactions
                                          join c in transactionCategories on t.transaction_id equals c.TransactionId into tmpC
                                          from c in tmpC.DefaultIfEmpty()
                                          select new
                                          {
                                              Transaction = t,
                                              Category = c == null
                                              ? null
                                              : new FinancialCategoryModel
                                              {
                                                  FinancialCategoryId = c.FinancialCategoryId,
                                                  Name = c.FinancialCategoryName
                                              }
                                          };
            return new GetBudgetTransactionsQueryResponse(
                Transactions: transactionCategoryData.Select(d =>
                {
                    var transaction = d.Transaction;
                    transaction.Category = d.Category;
                    return transaction;
                }).ToList(),
                ExpiredAccessItems: expiredAccessItems);
        }
    }
}
