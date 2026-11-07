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

    public class GetBudgetTransactionsQueryHandler(IConnectionService connectionService, IBudgetService budgetService,
        ICategoryService categoryService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<GetBudgetTransactionsQuery, GetBudgetTransactionsQueryResponse>
    {
        public async Task<GetBudgetTransactionsQueryResponse> Handle(GetBudgetTransactionsQuery request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId!;
            var budget = await budgetService.GetBudgetAsync(userId, request.BudgetId)
                ?? throw new NotFoundException("Budget", request.BudgetId);

            var accessItems = await connectionService.GetBudgetAccessItemsAsync(userId, budget.BudgetId);
            var transactions = await connectionService.GetTransactionsAsync(
                accessItems.Select(a => a.InstitutionAccessItemId).ToList(), budget.StartDate, budget.EndDate);

            var categories = (await categoryService.GetTransactionCategoriesAsync(userId, request.BudgetId))
                .GroupBy(c => c.TransactionId)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var transaction in transactions)
            {
                transaction.Category = categories.TryGetValue(transaction.transaction_id, out var c)
                    ? new FinancialCategoryModel { FinancialCategoryId = c.FinancialCategoryId, Name = c.FinancialCategoryName }
                    : null;
            }

            return new GetBudgetTransactionsQueryResponse(transactions,
                accessItems.Select(a => a.ExpiredAccessItem).OfType<ExpiredAccessItem>().ToList());
        }
    }
}
