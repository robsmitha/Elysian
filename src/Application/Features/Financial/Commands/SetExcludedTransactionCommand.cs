using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SetExcludedTransactionCommand(string TransactionId, int BudgetId) : IRequest<BudgetExcludedTransactionModel>;
    public class SetExcludedTransactionCommandHandler(IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SetExcludedTransactionCommand, BudgetExcludedTransactionModel>
    {
        public async Task<BudgetExcludedTransactionModel> Handle(SetExcludedTransactionCommand request, CancellationToken cancellationToken)
        {
            return await budgetService.SetExcludedTransactionAsync(claimsPrincipalAccessor.UserId, request.BudgetId, request.TransactionId);
        }
    }
}
