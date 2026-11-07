using Elysian.Application.Features.Financial.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>Monthly budgets, paychecks, recurring charges and spending trends for one person (Phase 3).</summary>
    public interface IBudgetMonthService
    {
        /// <summary>The month's numbers. Opening this month or a later one for the first time copies the latest earlier month's targets.</summary>
        Task<BudgetMonthModel> GetMonthAsync(string userId, int year, int month, DateOnly? today = null, CancellationToken cancellationToken = default);
        /// <summary>Replaces the month's targets with this list</summary>
        Task SaveTargetsAsync(string userId, int year, int month, IReadOnlyList<BudgetTargetSave> targets, CancellationToken cancellationToken = default);
        /// <summary>Copies another month's targets, or sets each target to the average spending of the 3 months before</summary>
        Task CopyTargetsAsync(string userId, int year, int month, int? fromYear, int? fromMonth, bool averageOfLast3, CancellationToken cancellationToken = default);
        /// <summary>Income typed in for the month (null goes back to paychecks)</summary>
        Task SetIncomeOverrideAsync(string userId, int year, int month, decimal? amount, CancellationToken cancellationToken = default);
        Task<List<SpendingTrendModel>> GetTrendsAsync(string userId, int months, DateOnly? today = null, CancellationToken cancellationToken = default);
        Task<List<RecurringModel>> GetRecurringAsync(string userId, DateOnly? today = null, CancellationToken cancellationToken = default);
        Task<IncomeStreamsModel> GetIncomeStreamsAsync(string userId, CancellationToken cancellationToken = default);
        Task<IncomeStreamModel> SaveIncomeStreamAsync(string userId, IncomeStreamModel model, CancellationToken cancellationToken = default);
        /// <summary>Stops counting a paycheck. The row stays so detection doesn't suggest it again.</summary>
        Task RemoveIncomeStreamAsync(string userId, int incomeStreamId, CancellationToken cancellationToken = default);
        Task<NotificationSettingsModel> GetNotificationSettingsAsync(string userId, CancellationToken cancellationToken = default);
        Task<NotificationSettingsModel> SaveNotificationSettingsAsync(string userId, NotificationSettingsModel model, CancellationToken cancellationToken = default);
    }
}
