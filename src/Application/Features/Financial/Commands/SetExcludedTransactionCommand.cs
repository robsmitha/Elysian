using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>The older budget page's "exempt": since Phase 2 it hides the transaction from budgets (all of them).</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SetExcludedTransactionCommand(string TransactionId, int BudgetId) : IRequest<BudgetExcludedTransactionModel>;

    public class SetExcludedTransactionCommandHandler(ITransactionService transactionService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<SetExcludedTransactionCommand, BudgetExcludedTransactionModel>
    {
        public async Task<BudgetExcludedTransactionModel> Handle(SetExcludedTransactionCommand request, CancellationToken cancellationToken)
        {
            await transactionService.SetTreatmentByPlaidIdAsync(claimsPrincipalAccessor.UserId!, request.TransactionId, Treatment.Hidden, cancellationToken);
            return new BudgetExcludedTransactionModel { TransactionId = request.TransactionId, BudgetId = request.BudgetId };
        }
    }
}
