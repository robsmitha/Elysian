using System.Globalization;
using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Budgeting
{
    public record DebtInput(int Id, string Name, decimal Balance, decimal Apr, decimal Minimum,
        decimal? PromoApr = null, DateOnly? PromoEndsOn = null, bool DeferredInterest = false);

    /// <summary>
    /// Month-by-month payoff (plan §4.4): add a month's interest (rounded to the cent), pay every minimum, then put the
    /// rest on debts in strategy order. Freed-up minimums roll forward because the monthly amount stays the same.
    /// </summary>
    public static class DebtPayoffCalculator
    {
        public const int MaxMonths = 600;

        /// <summary>The rate for a month: the promo rate while the promo is still running at the start of the month (D9)</summary>
        public static decimal RateFor(DebtInput d, DateOnly month) =>
            d.PromoApr is decimal promo && d.PromoEndsOn is DateOnly end && month < end ? promo : d.Apr;

        public static PayoffModel Simulate(IReadOnlyList<DebtInput> debts, decimal monthlyAmount, DebtStrategy strategy, DateOnly startMonth, IReadOnlyList<int>? customOrder = null)
        {
            startMonth = new DateOnly(startMonth.Year, startMonth.Month, 1);
            var balances = debts.ToDictionary(d => d.Id, d => Math.Max(0, d.Balance));
            var paidOff = debts.ToDictionary(d => d.Id, d => d.Balance <= 0 ? (DateOnly?)startMonth.AddMonths(-1) : null);
            var schedule = new List<DebtMonthModel>();
            var warnings = new List<string>();
            var totalInterest = 0m;
            var deferredWarned = new HashSet<int>();

            var minimums = debts.Where(d => d.Balance > 0).Sum(d => d.Minimum);
            if (monthlyAmount < minimums)
            {
                warnings.Add($"{Money(monthlyAmount)} a month is less than the minimums ({Money(minimums)}). Some minimums would be missed.");
            }
            foreach (var d in debts.Where(d => d.Balance > 0 && d.Minimum < Interest(d.Balance, RateFor(d, startMonth))))
            {
                warnings.Add($"{d.Name}'s minimum ({Money(d.Minimum)}) doesn't cover its monthly interest ({Money(Interest(d.Balance, RateFor(d, startMonth)))}).");
            }

            var month = startMonth;
            while (balances.Values.Any(b => b > 0) && schedule.Count < MaxMonths)
            {
                var lines = new Dictionary<int, (decimal Start, decimal Interest, decimal Payment)>();
                foreach (var d in debts)
                {
                    var start = balances[d.Id];
                    var interest = start > 0 ? Interest(start, RateFor(d, month)) : 0;
                    balances[d.Id] = start + interest;
                    totalInterest += interest;
                    lines[d.Id] = (start, interest, 0);
                }

                var order = Order(debts, balances, strategy, month, customOrder);
                var left = monthlyAmount;
                // Minimums first, in strategy order so a short month shorts the last ones
                foreach (var d in order)
                {
                    var pay = Math.Min(Math.Min(d.Minimum, balances[d.Id]), left);
                    Pay(d.Id, pay);
                }
                foreach (var d in order)
                {
                    if (left <= 0) break;
                    Pay(d.Id, Math.Min(left, balances[d.Id]));
                }

                void Pay(int id, decimal amount)
                {
                    if (amount <= 0) return;
                    balances[id] -= amount;
                    left -= amount;
                    var l = lines[id];
                    lines[id] = (l.Start, l.Interest, l.Payment + amount);
                }

                foreach (var d in debts)
                {
                    if (balances[d.Id] <= 0.005m && paidOff[d.Id] == null)
                    {
                        balances[d.Id] = 0;
                        paidOff[d.Id] = month;
                    }
                    // Deferred interest: the promo ends this month with money still owed
                    if (d.DeferredInterest && d.PromoEndsOn is DateOnly end && end >= month && end < month.AddMonths(1)
                        && balances[d.Id] > 0 && deferredWarned.Add(d.Id))
                    {
                        warnings.Add($"{d.Name} will still owe {Money(balances[d.Id])} when its promo ends on {end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}. With deferred interest, interest back to the start could be charged.");
                    }
                }

                var debtLines = debts.Select(d => new DebtLineModel(d.Id, lines[d.Id].Start, lines[d.Id].Interest, lines[d.Id].Payment, balances[d.Id])).ToList();
                schedule.Add(new DebtMonthModel(month,
                    debtLines.Sum(l => l.Start), debtLines.Sum(l => l.Interest), debtLines.Sum(l => l.Payment), debtLines.Sum(l => l.End), debtLines));
                month = month.AddMonths(1);
            }

            var never = balances.Values.Any(b => b > 0);
            if (never)
            {
                warnings.Add($"At {Money(monthlyAmount)} a month these debts never get paid off. Interest grows faster than the payments.");
            }

            var summary = new PayoffSummaryModel(strategy, monthlyAmount, never ? MaxMonths : schedule.Count,
                never || schedule.Count == 0 ? null : schedule[^1].Month, totalInterest, never);
            return new PayoffModel(summary, paidOff, schedule, warnings);
        }

        public static decimal Interest(decimal balance, decimal apr) => Math.Round(balance * apr / 1200m, 2, MidpointRounding.AwayFromZero);

        private static List<DebtInput> Order(IReadOnlyList<DebtInput> debts, Dictionary<int, decimal> balances, DebtStrategy strategy, DateOnly month, IReadOnlyList<int>? customOrder)
        {
            var open = debts.Where(d => balances[d.Id] > 0).ToList();
            // Avalanche uses each month's current rate, so a promo ending changes the order (D9)
            var avalanche = open.OrderByDescending(d => RateFor(d, month)).ThenBy(d => balances[d.Id]).ToList();
            return strategy switch
            {
                DebtStrategy.Snowball => open.OrderBy(d => balances[d.Id]).ThenByDescending(d => RateFor(d, month)).ToList(),
                DebtStrategy.Custom when customOrder != null =>
                    avalanche.OrderBy(d => customOrder.Contains(d.Id) ? customOrder.ToList().IndexOf(d.Id) : int.MaxValue).ToList(),
                _ => avalanche
            };
        }

        private static string Money(decimal amount) => amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    }
}
