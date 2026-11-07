using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>The older budget page's "restore": counts the transaction as spending again, whatever placed it outside.</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record RestoreExcludedTransactionCommand(string TransactionId, int BudgetId) : IRequest<bool>;

    public class RestoreExcludedTransactionCommandHandler(ITransactionService transactionService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<RestoreExcludedTransactionCommand, bool>
    {
        public async Task<bool> Handle(RestoreExcludedTransactionCommand request, CancellationToken cancellationToken)
        {
            await transactionService.SetTreatmentByPlaidIdAsync(claimsPrincipalAccessor.UserId!, request.TransactionId, Treatment.Spending, cancellationToken);
            return true;
        }
    }
}
