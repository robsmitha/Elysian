using Elysian.Application.Features.Financial.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>Savings goals for one person (Phase 5).</summary>
    public interface IGoalService
    {
        /// <summary>Goals with pace and the monthly amount each needs; starts new cycles for goals past their due date</summary>
        Task<GoalsModel> GetGoalsAsync(string userId, DateOnly? today = null, CancellationToken cancellationToken = default);
        Task<GoalModel> SaveGoalAsync(string userId, GoalModel model, DateOnly? today = null, CancellationToken cancellationToken = default);
        Task ArchiveGoalAsync(string userId, int savingsGoalId, CancellationToken cancellationToken = default);
        /// <summary>Money in (or out, negative), typed in or from a transfer. A transfer counts once, even if both sides are tagged.</summary>
        Task<GoalContributionModel> AddContributionAsync(string userId, AddContributionModel model, DateOnly? today = null, CancellationToken cancellationToken = default);
        Task RemoveContributionAsync(string userId, int goalContributionId, CancellationToken cancellationToken = default);
        /// <summary>The sum of each goal's monthly amount for a month: committed before safe to spend</summary>
        Task<decimal> CommittedForMonthAsync(string userId, DateOnly firstOfMonth, DateOnly today, CancellationToken cancellationToken = default);
    }
}
