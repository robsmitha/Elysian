using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using FluentValidation;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>The month's budget, income, safe to spend and alerts. Today comes from the browser so the day matches the person's time zone.</summary>
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetBudgetMonthQuery(int Year, int Month, DateOnly? Today = null) : IRequest<BudgetMonthModel>;

    public class GetBudgetMonthQueryHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<GetBudgetMonthQuery, BudgetMonthModel>
    {
        public Task<BudgetMonthModel> Handle(GetBudgetMonthQuery request, CancellationToken cancellationToken) =>
            budgets.GetMonthAsync(principal.UserId!, request.Year, request.Month, request.Today, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SaveBudgetTargetsCommand(int Year, int Month, List<BudgetTargetSave> Targets) : IRequest<Unit>;

    public class SaveBudgetTargetsCommandValidator : AbstractValidator<SaveBudgetTargetsCommand>
    {
        public SaveBudgetTargetsCommandValidator()
        {
            RuleFor(c => c.Targets).NotNull().Must(t => t == null || t.Count <= 200).WithMessage("At most 200 targets.");
        }
    }

    public class SaveBudgetTargetsCommandHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<SaveBudgetTargetsCommand, Unit>
    {
        public async Task<Unit> Handle(SaveBudgetTargetsCommand request, CancellationToken cancellationToken)
        {
            await budgets.SaveTargetsAsync(principal.UserId!, request.Year, request.Month, request.Targets, cancellationToken);
            return Unit.Value;
        }
    }

    /// <summary>Copy another month's targets, or use the average of the last 3 months' spending</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record CopyBudgetTargetsCommand(int Year, int Month, int? FromYear, int? FromMonth, bool AverageOfLast3) : IRequest<Unit>;

    public class CopyBudgetTargetsCommandHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<CopyBudgetTargetsCommand, Unit>
    {
        public async Task<Unit> Handle(CopyBudgetTargetsCommand request, CancellationToken cancellationToken)
        {
            await budgets.CopyTargetsAsync(principal.UserId!, request.Year, request.Month, request.FromYear, request.FromMonth, request.AverageOfLast3, cancellationToken);
            return Unit.Value;
        }
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SetIncomeOverrideCommand(int Year, int Month, decimal? Amount) : IRequest<Unit>;

    public class SetIncomeOverrideCommandHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<SetIncomeOverrideCommand, Unit>
    {
        public async Task<Unit> Handle(SetIncomeOverrideCommand request, CancellationToken cancellationToken)
        {
            await budgets.SetIncomeOverrideAsync(principal.UserId!, request.Year, request.Month, request.Amount, cancellationToken);
            return Unit.Value;
        }
    }

    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetSpendingTrendsQuery(int Months = 6, DateOnly? Today = null) : IRequest<List<SpendingTrendModel>>;

    public class GetSpendingTrendsQueryHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<GetSpendingTrendsQuery, List<SpendingTrendModel>>
    {
        public Task<List<SpendingTrendModel>> Handle(GetSpendingTrendsQuery request, CancellationToken cancellationToken) =>
            budgets.GetTrendsAsync(principal.UserId!, request.Months, request.Today, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetRecurringQuery(DateOnly? Today = null) : IRequest<List<RecurringModel>>;

    public class GetRecurringQueryHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<GetRecurringQuery, List<RecurringModel>>
    {
        public Task<List<RecurringModel>> Handle(GetRecurringQuery request, CancellationToken cancellationToken) =>
            budgets.GetRecurringAsync(principal.UserId!, request.Today, cancellationToken);
    }

    /// <summary>Paychecks set up, plus ones spotted in deposits that aren't set up yet</summary>
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetIncomeStreamsQuery : IRequest<IncomeStreamsModel>;

    public class GetIncomeStreamsQueryHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<GetIncomeStreamsQuery, IncomeStreamsModel>
    {
        public Task<IncomeStreamsModel> Handle(GetIncomeStreamsQuery request, CancellationToken cancellationToken) =>
            budgets.GetIncomeStreamsAsync(principal.UserId!, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SaveIncomeStreamCommand(IncomeStreamModel Stream) : IRequest<IncomeStreamModel>;

    public class SaveIncomeStreamCommandHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<SaveIncomeStreamCommand, IncomeStreamModel>
    {
        public Task<IncomeStreamModel> Handle(SaveIncomeStreamCommand request, CancellationToken cancellationToken) =>
            budgets.SaveIncomeStreamAsync(principal.UserId!, request.Stream, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record RemoveIncomeStreamCommand(int IncomeStreamId) : IRequest<Unit>;

    public class RemoveIncomeStreamCommandHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<RemoveIncomeStreamCommand, Unit>
    {
        public async Task<Unit> Handle(RemoveIncomeStreamCommand request, CancellationToken cancellationToken)
        {
            await budgets.RemoveIncomeStreamAsync(principal.UserId!, request.IncomeStreamId, cancellationToken);
            return Unit.Value;
        }
    }

    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetNotificationSettingsQuery : IRequest<NotificationSettingsModel>;

    public class GetNotificationSettingsQueryHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<GetNotificationSettingsQuery, NotificationSettingsModel>
    {
        public Task<NotificationSettingsModel> Handle(GetNotificationSettingsQuery request, CancellationToken cancellationToken) =>
            budgets.GetNotificationSettingsAsync(principal.UserId!, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SaveNotificationSettingsCommand(NotificationSettingsModel Settings) : IRequest<NotificationSettingsModel>;

    public class SaveNotificationSettingsCommandHandler(IBudgetMonthService budgets, IClaimsPrincipalAccessor principal) : IRequestHandler<SaveNotificationSettingsCommand, NotificationSettingsModel>
    {
        public Task<NotificationSettingsModel> Handle(SaveNotificationSettingsCommand request, CancellationToken cancellationToken) =>
            budgets.SaveNotificationSettingsAsync(principal.UserId!, request.Settings, cancellationToken);
    }
}
