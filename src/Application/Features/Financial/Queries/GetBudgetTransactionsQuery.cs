﻿using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
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
            var allTransactions = new List<TransactionModel>();
            var expiredAccessItems = new List<ExpiredAccessItem>();
            var budget = await budgetService.GetBudgetAsync(claimsPrincipalAccessor.UserId, request.BudgetId);
            var accessTokens = await accessTokenService.GetBudgetAccessTokensAsync(claimsPrincipalAccessor.UserId, budget.BudgetId);
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
                        var institution = await financialService.GetInstitutionAsync(accessToken.InstitutionId);
                        expiredAccessItems.Add(new ExpiredAccessItem(accessToken.AccessToken, fex.Error.error_message, institution));
                    }
                }
            }
            var transactionCategories = await categoryService.GetTransactionCategoriesAsync(claimsPrincipalAccessor.UserId, request.BudgetId);
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
