using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>
    /// The older budget page's "set category". Since Phase 2 the category belongs to the transaction itself (the same
    /// one the Transactions page shows); the budget gets the category as a line if it didn't have it.
    /// </summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SetTransactionCategoryCommand(string TransactionId, int CategoryId, int BudgetId) : IRequest<TransactionCategoryModel>;

    public class SetTransactionCategoryCommandHandler(ITransactionService transactionService, IBudgetService budgetService,
        IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SetTransactionCategoryCommand, TransactionCategoryModel>
    {
        public async Task<TransactionCategoryModel> Handle(SetTransactionCategoryCommand request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId!;
            await transactionService.SetCategoryByPlaidIdAsync(userId, request.TransactionId, request.CategoryId, cancellationToken);
            if (request.BudgetId > 0)
            {
                // Adds the line only when it's missing (and checks the budget is the person's)
                await budgetService.AddBudgetCategoryAsync(userId, request.BudgetId, request.CategoryId);
            }
            return new TransactionCategoryModel
            {
                TransactionId = request.TransactionId,
                FinancialCategoryId = request.CategoryId,
                BudgetId = request.BudgetId
            };
        }
    }
}
