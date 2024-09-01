using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize]
    public record RestoreExcludedTransactionCommand(string TransactionId, int BudgetId) : IRequest<bool>;
    public class RestoreExcludedTransactionCommandHandler(IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<RestoreExcludedTransactionCommand, bool>
    {
        public async Task<bool> Handle(RestoreExcludedTransactionCommand request, CancellationToken cancellationToken)
        {
            var success = await budgetService.RestoreExcludedTransactionAsync(claimsPrincipalAccessor.UserId, request.BudgetId, request.TransactionId);
            return success;
        }
    }
}
