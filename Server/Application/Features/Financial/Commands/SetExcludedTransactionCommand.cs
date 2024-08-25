using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    public class SetExcludedTransactionCommand(string transactionId, int budgetId) : IRequest<BudgetExcludedTransactionModel>
    {
        private string TransactionId { get; set; } = transactionId;
        private int BudgetId { get; set; } = budgetId;

        public class Handler(IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SetExcludedTransactionCommand, BudgetExcludedTransactionModel>
        {
            public async Task<BudgetExcludedTransactionModel> Handle(SetExcludedTransactionCommand request, CancellationToken cancellationToken)
            {
                return await budgetService.SetExcludedTransactionAsync(claimsPrincipalAccessor.UserId, request.BudgetId, request.TransactionId);
            }
        }
    }
}
