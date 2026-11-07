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
        ITransactionClassifier classifier, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<GetBudgetTransactionsQuery, GetBudgetTransactionsQueryResponse>
    {
        public async Task<GetBudgetTransactionsQueryResponse> Handle(GetBudgetTransactionsQuery request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId!;
            var budget = await budgetService.GetBudgetAsync(userId, request.BudgetId)
                ?? throw new NotFoundException("Budget", request.BudgetId);

            await classifier.ClassifyAsync(userId, cancellationToken: cancellationToken);
            var accessItems = await connectionService.GetBudgetAccessItemsAsync(userId, budget.BudgetId);
            var transactions = await connectionService.GetTransactionsAsync(
                accessItems.Select(a => a.InstitutionAccessItemId).ToList(), budget.StartDate, budget.EndDate);

            return new GetBudgetTransactionsQueryResponse(transactions,
                accessItems.Select(a => a.ExpiredAccessItem).OfType<ExpiredAccessItem>().ToList());
        }
    }
}
