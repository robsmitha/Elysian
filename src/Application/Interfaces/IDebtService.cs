using Elysian.Application.Features.Financial.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>Debts and the payoff plan for one person (Phase 4).</summary>
    public interface IDebtService
    {
        /// <summary>Debts (linked cards and loans are added automatically), both strategies, the chosen schedule, an optional what-if, and plan vs. actual</summary>
        Task<DebtPlanModel> GetPlanAsync(string userId, decimal extra = 0, DateOnly? today = null, CancellationToken cancellationToken = default);
        /// <summary>Edits APR, minimum, due day and promo terms (and balance for manual debts), or adds a manual debt</summary>
        Task<DebtModel> SaveDebtAsync(string userId, DebtModel model, CancellationToken cancellationToken = default);
        /// <summary>Deletes a manual debt, or leaves a linked one out of the plan</summary>
        Task RemoveDebtAsync(string userId, int debtId, CancellationToken cancellationToken = default);
        /// <summary>Saves the monthly amount and order, and freezes a new baseline schedule from today's balances</summary>
        Task SavePlanAsync(string userId, SaveDebtPlanModel model, DateOnly? today = null, CancellationToken cancellationToken = default);
    }
}
