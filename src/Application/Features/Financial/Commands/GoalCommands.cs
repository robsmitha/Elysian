using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetGoalsQuery(DateOnly? Today = null) : IRequest<GoalsModel>;

    public class GetGoalsQueryHandler(IGoalService goals, IClaimsPrincipalAccessor principal) : IRequestHandler<GetGoalsQuery, GoalsModel>
    {
        public Task<GoalsModel> Handle(GetGoalsQuery request, CancellationToken cancellationToken) =>
            goals.GetGoalsAsync(principal.UserId!, request.Today, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SaveGoalCommand(GoalModel Goal, DateOnly? Today = null) : IRequest<GoalModel>;

    public class SaveGoalCommandHandler(IGoalService goals, IClaimsPrincipalAccessor principal) : IRequestHandler<SaveGoalCommand, GoalModel>
    {
        public Task<GoalModel> Handle(SaveGoalCommand request, CancellationToken cancellationToken) =>
            goals.SaveGoalAsync(principal.UserId!, request.Goal, request.Today, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record ArchiveGoalCommand(int SavingsGoalId) : IRequest<Unit>;

    public class ArchiveGoalCommandHandler(IGoalService goals, IClaimsPrincipalAccessor principal) : IRequestHandler<ArchiveGoalCommand, Unit>
    {
        public async Task<Unit> Handle(ArchiveGoalCommand request, CancellationToken cancellationToken)
        {
            await goals.ArchiveGoalAsync(principal.UserId!, request.SavingsGoalId, cancellationToken);
            return Unit.Value;
        }
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record AddGoalContributionCommand(AddContributionModel Contribution, DateOnly? Today = null) : IRequest<GoalContributionModel>;

    public class AddGoalContributionCommandHandler(IGoalService goals, IClaimsPrincipalAccessor principal) : IRequestHandler<AddGoalContributionCommand, GoalContributionModel>
    {
        public Task<GoalContributionModel> Handle(AddGoalContributionCommand request, CancellationToken cancellationToken) =>
            goals.AddContributionAsync(principal.UserId!, request.Contribution, request.Today, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record RemoveGoalContributionCommand(int GoalContributionId) : IRequest<Unit>;

    public class RemoveGoalContributionCommandHandler(IGoalService goals, IClaimsPrincipalAccessor principal) : IRequestHandler<RemoveGoalContributionCommand, Unit>
    {
        public async Task<Unit> Handle(RemoveGoalContributionCommand request, CancellationToken cancellationToken)
        {
            await goals.RemoveContributionAsync(principal.UserId!, request.GoalContributionId, cancellationToken);
            return Unit.Value;
        }
    }
}
