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
    public class DebtService(ElysianContext context, TimeProvider clock) : IDebtService
    {
        /// <summary>Months of schedule sent to the page (a plan that never ends would otherwise be 600 rows)</summary>
        private const int ScheduleRowsReturned = 360;

        /// <summary>A planned month's start balance in the frozen baseline</summary>
        public record BaselineMonth(DateOnly Month, decimal Total, Dictionary<int, decimal> Debts);

        private DateOnly Today(DateOnly? fromClient)
        {
            var utc = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return fromClient is DateOnly d && Math.Abs(d.DayNumber - utc.DayNumber) <= 1 ? d : utc;
        }

        private static DateOnly FirstOf(DateOnly d) => new(d.Year, d.Month, 1);

        public async Task<DebtPlanModel> GetPlanAsync(string userId, decimal extra = 0, DateOnly? today = null, CancellationToken cancellationToken = default)
        {
            var now = Today(today);
            var month = FirstOf(now);
            await AddLinkedDebtsAsync(userId, cancellationToken);
            var debts = await LoadAsync(userId, cancellationToken);
            var plan = await context.DebtPlans.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            var customOrder = plan?.CustomOrderJson is string json ? JsonSerializer.Deserialize<List<int>>(json) ?? [] : [];

            var inputs = Inputs(debts);
            var minimums = inputs.Sum(d => d.Minimum);
            var monthly = plan?.MonthlyAmount ?? minimums;
            var strategy = plan?.Strategy ?? DebtStrategy.Avalanche;

            var avalanche = DebtPayoffCalculator.Simulate(inputs, monthly, DebtStrategy.Avalanche, month);
            var snowball = DebtPayoffCalculator.Simulate(inputs, monthly, DebtStrategy.Snowball, month);
            var selected = strategy switch
            {
                DebtStrategy.Snowball => snowball,
                DebtStrategy.Custom => DebtPayoffCalculator.Simulate(inputs, monthly, DebtStrategy.Custom, month, customOrder),
                _ => avalanche
            };
            var strategies = new List<PayoffSummaryModel> { avalanche.Summary, snowball.Summary };
            if (strategy == DebtStrategy.Custom) strategies.Add(selected.Summary);

            PayoffSummaryModel? whatIf = extra > 0
                ? DebtPayoffCalculator.Simulate(inputs, monthly + extra, strategy, month, customOrder).Summary
                : null;

            var models = debts.Select(d => ToModel(d, selected.PaidOff.GetValueOrDefault(d.Debt.DebtId))).ToList();
            var trimmed = selected with { Schedule = selected.Schedule.Take(ScheduleRowsReturned).ToList() };

            return new DebtPlanModel(
                Debts: models,
                Plan: plan == null ? null : new DebtPlanSettingsModel(plan.MonthlyAmount, plan.Strategy, customOrder, plan.StartDate),
                MonthlyAmount: monthly,
                TotalBalance: inputs.Sum(d => d.Balance),
                TotalMinimums: minimums,
                Strategies: strategies,
                Selected: trimmed,
                WhatIf: whatIf,
                History: plan == null ? [] : await HistoryAsync(plan, debts, month, cancellationToken));
        }

        public async Task<DebtModel> SaveDebtAsync(string userId, DebtModel model, CancellationToken cancellationToken = default)
        {
            if (model.Apr is < 0 or > 100) throw Invalid("Apr", "Enter an APR between 0% and 100%.");
            if (model.PromoApr is < 0 or > 100) throw Invalid("PromoApr", "Enter a promo APR between 0% and 100%.");
            if (model.PromoApr != null && model.PromoEndsOn == null) throw Invalid("PromoEndsOn", "Enter when the promo rate ends.");
            if (model.MinimumPayment is < 0 or > 1_000_000) throw Invalid("MinimumPayment", "Enter a minimum payment of $0 or more.");
            if (model.DueDay is < 1 or > 31) throw Invalid("DueDay", "The due day is a day of the month (1–31).");

            Debt debt;
            if (model.DebtId > 0)
            {
                debt = await context.Debts.FirstOrDefaultAsync(d => d.DebtId == model.DebtId && d.UserId == userId, cancellationToken)
                    ?? throw Invalid("DebtId", "That debt wasn't found.");
            }
            else
            {
                debt = context.Debts.Add(new Debt { UserId = userId, Source = DebtSource.Manual }).Entity;
            }

            if (debt.Source == DebtSource.Manual)
            {
                if (string.IsNullOrWhiteSpace(model.Name) || model.Name.Length > 100) throw Invalid("Name", "Give the debt a name (up to 100 characters).");
                if (model.Balance is < 0 or > 10_000_000) throw Invalid("Balance", "Enter the balance owed.");
                debt.Name = model.Name.Trim();
                if (debt.Balance != model.Balance || debt.BalanceAsOf == null) debt.BalanceAsOf = Today(null);
                debt.Balance = Math.Round(model.Balance, 2);
            }
            else if (!string.IsNullOrWhiteSpace(model.Name))
            {
                debt.Name = model.Name.Trim()[..Math.Min(100, model.Name.Trim().Length)];
            }

            debt.Apr = Math.Round(model.Apr, 3);
            debt.MinimumPayment = Math.Round(model.MinimumPayment, 2);
            debt.DueDay = model.DueDay;
            debt.PromoApr = model.PromoApr;
            debt.PromoEndsOn = model.PromoApr == null ? null : model.PromoEndsOn;
            debt.IsDeferredInterest = model.PromoApr != null && model.IsDeferredInterest;
            debt.IsActive = model.IsActive;
            debt.DetailsConfirmed = true;
            await context.SaveChangesAsync(cancellationToken);

            var loaded = (await LoadAsync(userId, cancellationToken)).First(d => d.Debt.DebtId == debt.DebtId);
            return ToModel(loaded, null);
        }

        public async Task RemoveDebtAsync(string userId, int debtId, CancellationToken cancellationToken = default)
        {
            var debt = await context.Debts.FirstOrDefaultAsync(d => d.DebtId == debtId && d.UserId == userId, cancellationToken)
                ?? throw Invalid("DebtId", "That debt wasn't found.");
            if (debt.Source == DebtSource.Manual) context.Debts.Remove(debt);
            else debt.IsActive = false;
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task SavePlanAsync(string userId, SaveDebtPlanModel model, DateOnly? today = null, CancellationToken cancellationToken = default)
        {
            if (model.MonthlyAmount <= 0 || model.MonthlyAmount > 1_000_000) throw Invalid("MonthlyAmount", "Enter how much goes to debts each month.");
            var month = FirstOf(Today(today));
            await AddLinkedDebtsAsync(userId, cancellationToken);
            var debts = await LoadAsync(userId, cancellationToken);
            var inputs = Inputs(debts);
            var order = model.Strategy == DebtStrategy.Custom ? model.CustomOrder ?? [] : [];
            if (order.Except(debts.Select(d => d.Debt.DebtId)).Any()) throw Invalid("CustomOrder", "That order includes a debt that wasn't found.");

            var run = DebtPayoffCalculator.Simulate(inputs, model.MonthlyAmount, model.Strategy, month, order);
            var baseline = run.Schedule.Select(r => new BaselineMonth(r.Month, r.Start, r.Debts.ToDictionary(l => l.DebtId, l => l.Start))).ToList();

            var plan = await context.DebtPlans.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
                ?? context.DebtPlans.Add(new DebtPlan { UserId = userId }).Entity;
            plan.MonthlyAmount = Math.Round(model.MonthlyAmount, 2);
            plan.Strategy = model.Strategy;
            plan.CustomOrderJson = model.Strategy == DebtStrategy.Custom ? JsonSerializer.Serialize(order) : null;
            plan.StartDate = month;
            plan.BaselineJson = JsonSerializer.Serialize(baseline);
            await context.SaveChangesAsync(cancellationToken);
        }

        // ---- loading ----

        private record Loaded(Debt Debt, decimal Balance, DateOnly? BalanceAsOf, bool IsStale, string? Detail);

        private IQueryable<FinancialAccount> MyAccounts(string userId) =>
            context.FinancialAccounts.Where(a => a.InstitutionAccessItem.RemovedAt == null
                && a.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId));

        /// <summary>Every linked credit card and loan becomes a debt once. The APR and minimum start as guesses to confirm.</summary>
        private async Task AddLinkedDebtsAsync(string userId, CancellationToken cancellationToken)
        {
            var accounts = await MyAccounts(userId)
                .Where(a => a.Type == "credit" || a.Type == "loan")
                .Where(a => !context.Debts.Any(d => d.UserId == userId && d.FinancialAccountId == a.FinancialAccountId))
                .Select(a => new { a.FinancialAccountId, a.Name, a.CurrentBalance })
                .ToListAsync(cancellationToken);
            foreach (var a in accounts)
            {
                var balance = a.CurrentBalance ?? 0;
                context.Debts.Add(new Debt
                {
                    UserId = userId,
                    FinancialAccountId = a.FinancialAccountId,
                    Source = DebtSource.Linked,
                    Name = a.Name.Length > 100 ? a.Name[..100] : a.Name,
                    // A typical card minimum until the real one is entered
                    MinimumPayment = Math.Max(25, Math.Round(balance * 0.02m)),
                });
            }
            if (accounts.Count > 0) await context.SaveChangesAsync(cancellationToken);
        }

        private async Task<List<Loaded>> LoadAsync(string userId, CancellationToken cancellationToken)
        {
            var rows = await context.Debts.Where(d => d.UserId == userId)
                .Select(d => new
                {
                    Debt = d,
                    Account = d.FinancialAccount == null ? null : new
                    {
                        d.FinancialAccount.CurrentBalance,
                        d.FinancialAccount.BalanceAsOf,
                        d.FinancialAccount.Mask,
                        d.FinancialAccount.InstitutionAccessItem.InstitutionName,
                        d.FinancialAccount.InstitutionAccessItem.Status,
                        d.FinancialAccount.InstitutionAccessItem.RemovedAt,
                    }
                })
                .ToListAsync(cancellationToken);

            return rows
                // A card whose bank was removed drops out
                .Where(r => r.Debt.Source == DebtSource.Manual || (r.Account != null && r.Account.RemovedAt == null))
                .Select(r => r.Account == null
                    ? new Loaded(r.Debt, r.Debt.Balance, r.Debt.BalanceAsOf, false, "entered by you")
                    : new Loaded(r.Debt, Math.Max(0, r.Account.CurrentBalance ?? 0),
                        r.Account.BalanceAsOf is DateTimeOffset at ? DateOnly.FromDateTime(at.UtcDateTime) : null,
                        r.Account.Status != ItemStatus.Healthy,
                        r.Account.Mask != null ? $"{r.Account.InstitutionName} ••{r.Account.Mask}" : r.Account.InstitutionName))
                .OrderByDescending(r => r.Debt.IsActive).ThenByDescending(r => r.Balance)
                .ToList();
        }

        private static List<DebtInput> Inputs(List<Loaded> debts) => debts
            .Where(d => d.Debt.IsActive && d.Balance > 0)
            .Select(d => new DebtInput(d.Debt.DebtId, d.Debt.Name, d.Balance, d.Debt.Apr, d.Debt.MinimumPayment, d.Debt.PromoApr, d.Debt.PromoEndsOn, d.Debt.IsDeferredInterest))
            .ToList();

        private static DebtModel ToModel(Loaded d, DateOnly? paidOff) => new()
        {
            DebtId = d.Debt.DebtId,
            Name = d.Debt.Name,
            Detail = d.Detail,
            FinancialAccountId = d.Debt.FinancialAccountId,
            Source = d.Debt.Source,
            Balance = d.Balance,
            BalanceAsOf = d.BalanceAsOf,
            IsStale = d.IsStale,
            Apr = d.Debt.Apr,
            MinimumPayment = d.Debt.MinimumPayment,
            DueDay = d.Debt.DueDay,
            PromoApr = d.Debt.PromoApr,
            PromoEndsOn = d.Debt.PromoEndsOn,
            IsDeferredInterest = d.Debt.IsDeferredInterest,
            DetailsConfirmed = d.Debt.DetailsConfirmed,
            IsActive = d.Debt.IsActive,
            PaidOffMonth = paidOff,
        };

        // ---- plan vs. actual ----

        /// <summary>
        /// Each month since the plan started: the baseline's planned balance against the actual one at the start of the month.
        /// A linked card's balance on a past date is today's balance minus its posted transactions since then (purchases and
        /// interest add, payments and refunds subtract). A manual debt has no history, so past months use its planned balance.
        /// </summary>
        private async Task<List<PlanVsActualModel>> HistoryAsync(DebtPlan plan, List<Loaded> debts, DateOnly thisMonth, CancellationToken cancellationToken)
        {
            var baseline = JsonSerializer.Deserialize<List<BaselineMonth>>(plan.BaselineJson) ?? [];
            if (baseline.Count == 0) return [];
            var inPlan = baseline[0].Debts.Keys.ToHashSet();
            var linked = debts.Where(d => inPlan.Contains(d.Debt.DebtId) && d.Debt.FinancialAccountId != null).ToList();
            var accountIds = linked.Select(d => d.Debt.FinancialAccountId!.Value).ToList();

            var transactions = await context.BankTransactions
                .Where(t => accountIds.Contains(t.FinancialAccountId) && t.RemovedAt == null && !t.Pending && t.Date >= plan.StartDate)
                .Select(t => new { t.FinancialAccountId, t.Date, t.Amount, t.Treatment })
                .ToListAsync(cancellationToken);

            var rows = new List<PlanVsActualModel>();
            for (var month = plan.StartDate; month <= thisMonth; month = month.AddMonths(1))
            {
                var planned = baseline.FirstOrDefault(b => b.Month == month);
                var plannedTotal = planned?.Total ?? 0;
                var payments = -transactions
                    .Where(t => t.Date >= month && t.Date < month.AddMonths(1) && t.Amount < 0
                        && (t.Treatment == Treatment.CardPayment || t.Treatment == Treatment.Transfer))
                    .Sum(t => t.Amount);
                decimal? shortfall = month < thisMonth && payments < plan.MonthlyAmount - 0.5m ? plan.MonthlyAmount - payments : null;

                if (month == plan.StartDate)
                {
                    rows.Add(new PlanVsActualModel(month, plannedTotal, plannedTotal, payments, "Baseline", 0, shortfall));
                    continue;
                }

                var actual = 0m;
                foreach (var id in inPlan)
                {
                    var debt = debts.FirstOrDefault(d => d.Debt.DebtId == id);
                    if (debt == null) continue;
                    if (debt.Debt.FinancialAccountId is int accountId)
                    {
                        var since = transactions.Where(t => t.FinancialAccountId == accountId && t.Date >= month).Sum(t => t.Amount);
                        actual += Math.Max(0, debt.Balance - since);
                    }
                    else
                    {
                        actual += month == thisMonth ? debt.Balance : planned?.Debts.GetValueOrDefault(id) ?? 0;
                    }
                }

                var difference = plannedTotal - actual;
                var status = Math.Abs(difference) < 1 ? "OnTrack" : difference > 0 ? "Ahead" : "Behind";
                rows.Add(new PlanVsActualModel(month, plannedTotal, actual, payments, status, difference, shortfall));
            }
            return rows;
        }

        private static CustomValidationException Invalid(string field, string message) =>
            new([new FluentValidation.Results.ValidationFailure(field, message)]);
    }
}
