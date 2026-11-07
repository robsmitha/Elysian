using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>Debts, both strategies, the chosen schedule and plan vs. actual. Extra adds a what-if amount per month.</summary>
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetDebtPlanQuery(decimal Extra = 0, DateOnly? Today = null) : IRequest<DebtPlanModel>;

    public class GetDebtPlanQueryHandler(IDebtService debts, IClaimsPrincipalAccessor principal) : IRequestHandler<GetDebtPlanQuery, DebtPlanModel>
    {
        public Task<DebtPlanModel> Handle(GetDebtPlanQuery request, CancellationToken cancellationToken) =>
            debts.GetPlanAsync(principal.UserId!, Math.Clamp(request.Extra, 0, 1_000_000), request.Today, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SaveDebtCommand(DebtModel Debt) : IRequest<DebtModel>;

    public class SaveDebtCommandHandler(IDebtService debts, IClaimsPrincipalAccessor principal) : IRequestHandler<SaveDebtCommand, DebtModel>
    {
        public Task<DebtModel> Handle(SaveDebtCommand request, CancellationToken cancellationToken) =>
            debts.SaveDebtAsync(principal.UserId!, request.Debt, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record RemoveDebtCommand(int DebtId) : IRequest<Unit>;

    public class RemoveDebtCommandHandler(IDebtService debts, IClaimsPrincipalAccessor principal) : IRequestHandler<RemoveDebtCommand, Unit>
    {
        public async Task<Unit> Handle(RemoveDebtCommand request, CancellationToken cancellationToken)
        {
            await debts.RemoveDebtAsync(principal.UserId!, request.DebtId, cancellationToken);
            return Unit.Value;
        }
    }

    /// <summary>Saves the monthly amount and strategy, and freezes a new baseline</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SaveDebtPlanCommand(SaveDebtPlanModel Plan, DateOnly? Today = null) : IRequest<Unit>;

    public class SaveDebtPlanCommandHandler(IDebtService debts, IClaimsPrincipalAccessor principal) : IRequestHandler<SaveDebtPlanCommand, Unit>
    {
        public async Task<Unit> Handle(SaveDebtPlanCommand request, CancellationToken cancellationToken)
        {
            await debts.SavePlanAsync(principal.UserId!, request.Plan, request.Today, cancellationToken);
            return Unit.Value;
        }
    }
}
