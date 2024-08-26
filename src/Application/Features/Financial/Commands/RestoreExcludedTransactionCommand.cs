using Elysian.Application.Interfaces;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    public class RestoreExcludedTransactionCommand(string transactionId, int budgetId) : IRequest<bool>
    {
        private string TransactionId { get; set; } = transactionId;
        private int BudgetId { get; set; } = budgetId;

        public class Handler(IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<RestoreExcludedTransactionCommand, bool>
        {
            public async Task<bool> Handle(RestoreExcludedTransactionCommand request, CancellationToken cancellationToken)
            {
                var success = await budgetService.RestoreExcludedTransactionAsync(claimsPrincipalAccessor.UserId, request.BudgetId, request.TransactionId);
                return success;
            }
        }
    }
}
