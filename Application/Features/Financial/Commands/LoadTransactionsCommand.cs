using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Financial.Commands
{
    public class LoadTransactionsCommand : IRequest<LoadTransactionsCommand.Response>
    {
        private string UserId { get; set; }
        private int BudgetId { get; set; }
        public LoadTransactionsCommand(string userId, int budgetId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException($"{nameof(userId)} cannot be null or empty.");
            }

            UserId = userId;
            BudgetId = budgetId;
        }

        public class Handler(ILogger<LoadTransactionsCommand> logger, IFinancialService financialService,
            IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<LoadTransactionsCommand, Response>
        {
            public async Task<Response> Handle(LoadTransactionsCommand request, CancellationToken cancellationToken)
            {
                var budget = await budgetService.GetBudgetAsync(claimsPrincipalAccessor.UserId, request.BudgetId);
                var transactions = await financialService.GetTransactionsAsync(request.UserId, budget.StartDate, budget.EndDate);
                return new Response(transactions);
            }
        }

        public class Response
        {
            public List<TransactionModel> Transactions { get; set; }
            public Response(List<TransactionModel> transactions = null)
            {
                Transactions = transactions ?? new List<TransactionModel>();
            }
        }
    }
}
