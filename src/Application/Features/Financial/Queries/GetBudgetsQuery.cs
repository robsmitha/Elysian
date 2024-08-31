using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize]
    public record GetBudgetsQuery : IRequest<List<BudgetModel>>;
    public class GetBudgetsQueryHandler(IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<GetBudgetsQuery, List<BudgetModel>>
    {
        public async Task<List<BudgetModel>> Handle(GetBudgetsQuery request, CancellationToken cancellationToken)
        {
            return await budgetService.GetBudgetsAsync(claimsPrincipalAccessor.UserId);
        }
    }
}
