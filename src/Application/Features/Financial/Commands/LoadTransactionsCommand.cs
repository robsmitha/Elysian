using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize]
    public record LoadTransactionsCommand(string UserId, int BudgetId) : IRequest<LoadTransactionsCommandResponse>;

    public class LoadTransactionsCommandHandler(ILogger<LoadTransactionsCommand> logger, IFinancialService financialService,
        IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<LoadTransactionsCommand, LoadTransactionsCommandResponse>
    {
        public async Task<LoadTransactionsCommandResponse> Handle(LoadTransactionsCommand request, CancellationToken cancellationToken)
        {
            var budget = await budgetService.GetBudgetAsync(claimsPrincipalAccessor.UserId, request.BudgetId);
            var transactions = await financialService.GetTransactionsAsync(request.UserId, budget.StartDate, budget.EndDate);
            return new LoadTransactionsCommandResponse(transactions);
        }
    }

    public class LoadTransactionsCommandResponse
    {
        public List<TransactionModel> Transactions { get; set; }
        public LoadTransactionsCommandResponse(List<TransactionModel> transactions = null)
        {
            Transactions = transactions ?? new List<TransactionModel>();
        }
    }
}
