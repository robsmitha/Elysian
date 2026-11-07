using System.Text.Json;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Budgeting;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    public class GoalService(ElysianContext context, TimeProvider clock) : IGoalService
    {
        /// <summary>How far back transfers are offered for tagging</summary>
        private const int TransferLookbackDays = 60;
        /// <summary>Two sides of one transfer land within this many days of each other</summary>
        private const int TransferPairDays = 3;

        private DateOnly Today(DateOnly? fromClient)
        {
            var utc = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return fromClient is DateOnly d && Math.Abs(d.DayNumber - utc.DayNumber) <= 1 ? d : utc;
        }

        private static DateOnly FirstOf(DateOnly d) => new(d.Year, d.Month, 1);

        private IQueryable<FinancialAccount> MyAccounts(string userId) =>
            context.FinancialAccounts.Where(a => a.InstitutionAccessItem.RemovedAt == null
                && a.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId));

        private IQueryable<BankTransaction> MyTransactions(string userId) =>
            context.BankTransactions.Where(t => t.RemovedAt == null
                && t.FinancialAccount.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId));

        public async Task<GoalsModel> GetGoalsAsync(string userId, DateOnly? today = null, CancellationToken cancellationToken = default)
        {
            var now = Today(today);
            var goals = await LoadAsync(userId, now, cancellationToken);
            var models = new List<GoalModel>();
            foreach (var g in goals)
            {
                models.Add(await ToModelAsync(g, now, FirstOf(now), cancellationToken));
            }

            // D10: shared goals kept in one account shouldn't claim more than the account holds
            var linked = goals.Where(g => g.LinkedAccountId != null).Select(g => g.LinkedAccountId!.Value).ToList();
            var savings = await MyAccounts(userId)
                .Where(a => a.BudgetRole == AccountBudgetRole.Savings || linked.Contains(a.FinancialAccountId))
                .Select(a => new { a.FinancialAccountId, a.Name, a.Mask, Balance = a.CurrentBalance ?? 0 })
                .ToListAsync(cancellationToken);
            var accounts = savings.Select(a =>
            {
                var allocated = models.Where(m => m.LinkedAccountId == a.FinancialAccountId && m.FundingMode == GoalFundingMode.Shared).Sum(m => m.Saved);
                return new GoalAccountModel(a.FinancialAccountId, a.Mask != null ? $"{a.Name} ••{a.Mask}" : a.Name, a.Balance, allocated, a.Balance - allocated);
            }).ToList();

            return new GoalsModel(
                Goals: models,
                NeededThisMonth: models.Sum(m => m.RequiredMonthly),
                TotalSaved: models.Sum(m => m.Saved),
                OnTrack: models.Count(m => m.Status is GoalStatus.OnTrack or GoalStatus.Ahead or GoalStatus.Funded),
                Accounts: accounts,
                Transfers: await UntaggedTransfersAsync(userId, now, savings.Select(a => a.FinancialAccountId).ToList(), cancellationToken));
        }

        public async Task<GoalModel> SaveGoalAsync(string userId, GoalModel model, DateOnly? today = null, CancellationToken cancellationToken = default)
        {
            var now = Today(today);
            if (string.IsNullOrWhiteSpace(model.Name) || model.Name.Length > 100) throw Invalid("Name", "Give the goal a name (up to 100 characters).");
            if (model.TargetAmount <= 0 || model.TargetAmount > 100_000_000) throw Invalid("TargetAmount", "Enter how much the goal needs.");
            if (model.StartingBalance < 0) throw Invalid("StartingBalance", "Already saved can't be negative.");
            if (model.LinkedAccountId is int accountId && !await MyAccounts(userId).AnyAsync(a => a.FinancialAccountId == accountId, cancellationToken))
                throw Invalid("LinkedAccountId", "That account wasn't found.");
            if (model.FundingMode == GoalFundingMode.Dedicated && model.LinkedAccountId == null)
                throw Invalid("LinkedAccountId", "Choose the account this goal has to itself.");

            SavingsGoal goal;
            if (model.SavingsGoalId > 0)
            {
                goal = await context.SavingsGoals.FirstOrDefaultAsync(g => g.SavingsGoalId == model.SavingsGoalId && g.UserId == userId, cancellationToken)
                    ?? throw Invalid("SavingsGoalId", "That goal wasn't found.");
            }
            else
            {
                // A new goal's even-pace line starts this month from what's already saved
                goal = context.SavingsGoals.Add(new SavingsGoal { UserId = userId, CycleStart = FirstOf(now) }).Entity;
            }
            if (model.DueDate < goal.CycleStart) throw Invalid("DueDate", "The due date has to be after the goal starts.");

            goal.Name = model.Name.Trim();
            goal.Icon = string.IsNullOrWhiteSpace(model.Icon) ? null : model.Icon.Trim();
            goal.TargetAmount = Math.Round(model.TargetAmount, 2);
            goal.DueDate = model.DueDate;
            goal.Cycle = model.Cycle;
            goal.CycleEnd = model.CycleEnd;
            goal.FundingMode = model.FundingMode;
            goal.LinkedAccountId = model.LinkedAccountId;
            goal.StartingBalance = Math.Round(model.StartingBalance, 2);
            await context.SaveChangesAsync(cancellationToken);

            var loaded = (await LoadAsync(userId, now, cancellationToken)).First(g => g.SavingsGoalId == goal.SavingsGoalId);
            return await ToModelAsync(loaded, now, FirstOf(now), cancellationToken);
        }

        public async Task ArchiveGoalAsync(string userId, int savingsGoalId, CancellationToken cancellationToken = default)
        {
            var goal = await context.SavingsGoals.FirstOrDefaultAsync(g => g.SavingsGoalId == savingsGoalId && g.UserId == userId, cancellationToken)
                ?? throw Invalid("SavingsGoalId", "That goal wasn't found.");
            goal.IsArchived = true;
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<GoalContributionModel> AddContributionAsync(string userId, AddContributionModel model, DateOnly? today = null, CancellationToken cancellationToken = default)
        {
            var goal = await context.SavingsGoals.FirstOrDefaultAsync(g => g.SavingsGoalId == model.SavingsGoalId && g.UserId == userId && !g.IsArchived, cancellationToken)
                ?? throw Invalid("SavingsGoalId", "That goal wasn't found.");
            if (goal.FundingMode == GoalFundingMode.Dedicated)
                throw Invalid("SavingsGoalId", "This goal follows its account's balance, so there's nothing to log.");

            GoalContribution contribution;
            if (model.BankTransactionId is long id)
            {
                var t = await MyTransactions(userId).Where(x => x.BankTransactionId == id)
                    .Select(x => new { x.BankTransactionId, x.Amount, x.Date, x.FinancialAccountId, x.Name, x.MerchantName })
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw Invalid("BankTransactionId", "That transaction wasn't found.");

                // The same transfer, from either side, counts once
                var earliest = t.Date.AddDays(-TransferPairDays);
                var latest = t.Date.AddDays(TransferPairDays);
                var existing = await context.GoalContributions
                    .Where(c => c.SavingsGoal.UserId == userId && c.BankTransactionId != null)
                    .Join(context.BankTransactions, c => c.BankTransactionId, b => b.BankTransactionId, (c, b) => new { c, b })
                    .Where(x => x.b.BankTransactionId == t.BankTransactionId
                        || (x.b.Amount == -t.Amount && x.b.FinancialAccountId != t.FinancialAccountId && x.b.Date >= earliest && x.b.Date <= latest))
                    .Select(x => x.c)
                    .FirstOrDefaultAsync(cancellationToken);
                if (existing != null)
                {
                    if (existing.SavingsGoalId != goal.SavingsGoalId)
                        throw Invalid("BankTransactionId", "That transfer already counts toward another goal.");
                    return ToModel(existing);
                }

                contribution = new GoalContribution
                {
                    SavingsGoalId = goal.SavingsGoalId,
                    Date = t.Date,
                    Amount = Math.Abs(t.Amount),
                    BankTransactionId = t.BankTransactionId,
                    Source = ContributionSource.Transfer,
                    Note = t.MerchantName ?? t.Name,
                };
            }
            else
            {
                if (model.Amount is not decimal amount || amount == 0 || Math.Abs(amount) > 100_000_000)
                    throw Invalid("Amount", "Enter an amount (negative takes money out).");
                contribution = new GoalContribution
                {
                    SavingsGoalId = goal.SavingsGoalId,
                    Date = model.Date ?? Today(today),
                    Amount = Math.Round(amount, 2),
                    Source = ContributionSource.Manual,
                    Note = model.Note?.Trim() is { Length: > 0 } note ? note[..Math.Min(200, note.Length)] : null,
                };
            }
            context.GoalContributions.Add(contribution);
            await context.SaveChangesAsync(cancellationToken);
            return ToModel(contribution);
        }

        public async Task RemoveContributionAsync(string userId, int goalContributionId, CancellationToken cancellationToken = default)
        {
            var c = await context.GoalContributions.FirstOrDefaultAsync(x => x.GoalContributionId == goalContributionId && x.SavingsGoal.UserId == userId, cancellationToken)
                ?? throw Invalid("GoalContributionId", "That contribution wasn't found.");
            context.GoalContributions.Remove(c);
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<decimal> CommittedForMonthAsync(string userId, DateOnly firstOfMonth, DateOnly today, CancellationToken cancellationToken = default)
        {
            var last = firstOfMonth.AddMonths(1).AddDays(-1);
            var goals = await LoadAsync(userId, today, cancellationToken);
            var total = 0m;
            foreach (var g in goals.Where(g => g.CycleStart <= last && g.DueDate >= firstOfMonth))
            {
                total += (await ToModelAsync(g, firstOfMonth, firstOfMonth, cancellationToken)).RequiredMonthly;
            }
            return total;
        }

        // ---- loading and cycles ----

        private async Task<List<SavingsGoal>> LoadAsync(string userId, DateOnly now, CancellationToken cancellationToken)
        {
            var goals = await context.SavingsGoals.Include(g => g.Contributions).Include(g => g.LinkedAccount)
                .Where(g => g.UserId == userId && !g.IsArchived)
                .OrderBy(g => g.DueDate)
                .ToListAsync(cancellationToken);
            if (await AdvanceCyclesAsync(goals, now, cancellationToken)) await context.SaveChangesAsync(cancellationToken);
            return goals;
        }

        /// <summary>
        /// A repeating goal past its due date starts its next cycle: the finished one goes into history, and the new one
        /// starts at $0 (Reset) or with what was saved over the target (RollOverSurplus).
        /// </summary>
        private async Task<bool> AdvanceCyclesAsync(List<SavingsGoal> goals, DateOnly now, CancellationToken cancellationToken)
        {
            var changed = false;
            foreach (var g in goals)
            {
                while (GoalCalculator.CycleMonths(g.Cycle) is int months && now > g.DueDate)
                {
                    var saved = g.FundingMode == GoalFundingMode.Dedicated
                        ? await AccountBalanceOnAsync(g, g.DueDate.AddDays(1), cancellationToken)
                        : g.StartingBalance + g.Contributions.Where(c => c.Date >= g.CycleStart && c.Date <= g.DueDate).Sum(c => c.Amount);
                    var past = JsonSerializer.Deserialize<List<PastCycleModel>>(g.PastCyclesJson) ?? [];
                    past.Add(new PastCycleModel(g.CycleStart, g.DueDate, g.TargetAmount, saved));
                    g.PastCyclesJson = JsonSerializer.Serialize(past);

                    g.StartingBalance = g.CycleEnd == GoalCycleEnd.RollOverSurplus ? Math.Max(0, saved - g.TargetAmount) : 0;
                    g.CycleStart = g.DueDate.AddDays(1);
                    g.DueDate = GoalCalculator.NextDue(g.DueDate, months);
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>A linked account's balance at the start of a day: today's balance plus everything that left it since (money out is positive)</summary>
        private async Task<decimal> AccountBalanceOnAsync(SavingsGoal g, DateOnly date, CancellationToken cancellationToken)
        {
            if (g.LinkedAccount == null) return 0;
            var since = await context.BankTransactions
                .Where(t => t.FinancialAccountId == g.LinkedAccount.FinancialAccountId && t.RemovedAt == null && !t.Pending && t.Date >= date)
                .SumAsync(t => t.Amount, cancellationToken);
            return Math.Max(0, (g.LinkedAccount.CurrentBalance ?? 0) + since);
        }

        private async Task<GoalModel> ToModelAsync(SavingsGoal g, DateOnly now, DateOnly monthStart, CancellationToken cancellationToken)
        {
            decimal saved, atMonthStart;
            if (g.FundingMode == GoalFundingMode.Dedicated)
            {
                saved = Math.Max(0, g.LinkedAccount?.CurrentBalance ?? 0);
                atMonthStart = await AccountBalanceOnAsync(g, monthStart, cancellationToken);
            }
            else
            {
                var cycle = g.Contributions.Where(c => c.Date >= g.CycleStart).ToList();
                saved = g.StartingBalance + cycle.Sum(c => c.Amount);
                atMonthStart = g.StartingBalance + cycle.Where(c => c.Date < monthStart).Sum(c => c.Amount);
            }

            var result = GoalCalculator.Calculate(new GoalCalculator.Input(g.TargetAmount, g.CycleStart, g.DueDate, g.Cycle, g.StartingBalance,
                saved, atMonthStart, saved - atMonthStart), now);

            return new GoalModel
            {
                SavingsGoalId = g.SavingsGoalId,
                Name = g.Name,
                Icon = g.Icon,
                TargetAmount = g.TargetAmount,
                DueDate = g.DueDate,
                CycleStart = g.CycleStart,
                Cycle = g.Cycle,
                CycleEnd = g.CycleEnd,
                FundingMode = g.FundingMode,
                LinkedAccountId = g.LinkedAccountId,
                LinkedAccountName = g.LinkedAccount == null ? null : g.LinkedAccount.Mask != null ? $"{g.LinkedAccount.Name} ••{g.LinkedAccount.Mask}" : g.LinkedAccount.Name,
                StartingBalance = g.StartingBalance,
                IsArchived = g.IsArchived,
                Saved = saved,
                SavedAtMonthStart = atMonthStart,
                ExpectedByNow = result.ExpectedByNow,
                RequiredMonthly = result.RequiredMonthly,
                MonthsLeft = result.MonthsLeft,
                ContributedThisMonth = saved - atMonthStart,
                Status = result.Status,
                Difference = result.Difference,
                Contributions = g.Contributions.OrderByDescending(c => c.Date).ThenByDescending(c => c.GoalContributionId).Take(12).Select(ToModel).ToList(),
                PastCycles = JsonSerializer.Deserialize<List<PastCycleModel>>(g.PastCyclesJson) ?? [],
            };
        }

        /// <summary>Money into savings accounts lately that isn't counted toward a goal (from either side of the transfer)</summary>
        private async Task<List<GoalTransferModel>> UntaggedTransfersAsync(string userId, DateOnly now, List<int> accountIds, CancellationToken cancellationToken)
        {
            if (accountIds.Count == 0) return [];
            var from = now.AddDays(-TransferLookbackDays);
            var incoming = await MyTransactions(userId)
                .Where(t => accountIds.Contains(t.FinancialAccountId) && t.Amount < 0 && t.Date >= from && t.Treatment == Treatment.Transfer)
                .Select(t => new { t.BankTransactionId, t.Date, t.Amount, t.FinancialAccountId, AccountName = t.FinancialAccount.Name, Name = t.MerchantName ?? t.Name })
                .ToListAsync(cancellationToken);
            var tagged = await context.GoalContributions
                .Where(c => c.SavingsGoal.UserId == userId && c.BankTransactionId != null && c.Date >= from.AddDays(-TransferPairDays))
                .Join(context.BankTransactions, c => c.BankTransactionId, b => b.BankTransactionId, (c, b) => new { b.BankTransactionId, b.Amount, b.Date, b.FinancialAccountId })
                .ToListAsync(cancellationToken);

            return incoming
                .Where(t => !tagged.Any(x => x.BankTransactionId == t.BankTransactionId
                    || (x.Amount == -t.Amount && x.FinancialAccountId != t.FinancialAccountId && Math.Abs(x.Date.DayNumber - t.Date.DayNumber) <= TransferPairDays)))
                .OrderByDescending(t => t.Date)
                .Select(t => new GoalTransferModel(t.BankTransactionId, t.Date, -t.Amount, t.AccountName, t.Name))
                .ToList();
        }

        private static GoalContributionModel ToModel(GoalContribution c) =>
            new(c.GoalContributionId, c.Date, c.Amount, c.Source, c.Note, c.BankTransactionId);

        private static CustomValidationException Invalid(string field, string message) =>
            new([new FluentValidation.Results.ValidationFailure(field, message)]);
    }
}
