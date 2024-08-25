using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    public class GetBudgetsQuery : IRequest<List<BudgetModel>>
    {
        public class Handler(IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<GetBudgetsQuery, List<BudgetModel>>
        {
            public async Task<List<BudgetModel>> Handle(GetBudgetsQuery request, CancellationToken cancellationToken)
            {
                return await budgetService.GetBudgetsAsync(claimsPrincipalAccessor.UserId);
            }
        }
    }
}
