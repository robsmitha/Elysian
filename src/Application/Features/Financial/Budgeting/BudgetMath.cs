using System.Globalization;
using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Budgeting
{
    /// <summary>Money in on a spending or income account, already treated as income. Amount is positive.</summary>
    public record Deposit(long Id, DateOnly Date, decimal Amount, string MerchantKey, string Name, string? AccountName);

    public record StreamInfo(int Id, string Name, string MerchantKey, PayFrequency Frequency, DateOnly Anchor, decimal ExpectedAmount);

    /// <summary>Money out. Amount is positive.</summary>
    public record Charge(DateOnly Date, decimal Amount, string MerchantKey, string Name, int? CategoryId, string? CategoryName, Treatment Treatment, bool Counted);

    public record CategoryInfo(int Id, string Name, string? Icon, CategoryGroup Group, int SortOrder);

    public record TargetInput(int CategoryId, decimal Amount, bool Rollover, decimal Carry);

    /// <summary>One counted amount in one category (a split contributes one line per part). Refunds are negative.</summary>
    public record SpendLine(int? CategoryId, decimal Amount, bool Pending, string Merchant);

    internal static class Stats
    {
        public static decimal Median(IEnumerable<decimal> values)
        {
            var v = values.OrderBy(x => x).ToList();
            if (v.Count == 0) return 0;
            return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
        }

        public static double Median(IEnumerable<int> values) => (double)Median(values.Select(x => (decimal)x));

        public static DateOnly LastDay(int year, int month) => new(year, month, DateTime.DaysInMonth(year, month));

        public static string Title(string text) =>
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.Trim().ToLowerInvariant());
    }

    /// <summary>When paychecks land. Semimonthly and monthly paydays on a weekend move to the Friday before.</summary>
    public static class PaySchedule
    {
        public static IEnumerable<DateOnly> Paydays(PayFrequency frequency, DateOnly anchor, DateOnly from, DateOnly to)
        {
            if (to < from) yield break;
            if (frequency is PayFrequency.Weekly or PayFrequency.Biweekly)
            {
                var step = frequency == PayFrequency.Weekly ? 7 : 14;
                var k = (int)Math.Ceiling((from.DayNumber - anchor.DayNumber) / (double)step);
                for (var d = anchor.AddDays(k * step); d <= to; d = d.AddDays(step)) yield return d;
                yield break;
            }
            for (var m = new DateOnly(from.Year, from.Month, 1).AddMonths(-1); m <= to.AddMonths(1); m = m.AddMonths(1))
            {
                var last = DateTime.DaysInMonth(m.Year, m.Month);
                var days = frequency == PayFrequency.Semimonthly ? new[] { 15, last } : new[] { Math.Min(anchor.Day, last) };
                foreach (var day in days)
                {
                    var d = PreviousWeekday(new DateOnly(m.Year, m.Month, day));
                    if (d >= from && d <= to) yield return d;
                }
            }
        }

        public static List<DateOnly> InMonth(PayFrequency frequency, DateOnly anchor, int year, int month) =>
            Paydays(frequency, anchor, new DateOnly(year, month, 1), Stats.LastDay(year, month)).ToList();

        /// <summary>The first month after this one with more paydays than usual (3 biweekly, 5 weekly), or null.</summary>
        public static DateOnly? NextExtraPaycheckMonth(PayFrequency frequency, DateOnly anchor, int year, int month)
        {
            if (frequency is not (PayFrequency.Weekly or PayFrequency.Biweekly)) return null;
            var usual = frequency == PayFrequency.Weekly ? 4 : 2;
            for (var m = new DateOnly(year, month, 1).AddMonths(1); m < new DateOnly(year, month, 1).AddMonths(25); m = m.AddMonths(1))
            {
                if (InMonth(frequency, anchor, m.Year, m.Month).Count > usual) return m;
            }
            return null;
        }

        private static DateOnly PreviousWeekday(DateOnly d) => d.DayOfWeek switch
        {
            DayOfWeek.Saturday => d.AddDays(-1),
            DayOfWeek.Sunday => d.AddDays(-2),
            _ => d
        };
    }

    public static class IncomeCalculator
    {
        /// <summary>A deposit this many days either side of a payday is that paycheck</summary>
        public const int MatchWindowDays = 4;

        /// <summary>
        /// Paychecks count in the month of their scheduled payday (D3). Received ones count at their actual amount; ones still
        /// to come count at the expected amount; a payday more than a few days past with no deposit isn't counted. Other money
        /// treated as income counts in the month it arrived. A typed-in amount for the month replaces all of it.
        /// </summary>
        public static IncomeModel ForMonth(int year, int month, DateOnly today, IReadOnlyList<StreamInfo> streams, IReadOnlyList<Deposit> deposits, decimal? incomeOverride)
        {
            var first = new DateOnly(year, month, 1);
            var last = Stats.LastDay(year, month);
            var used = new HashSet<long>();
            var paychecks = new List<PaycheckModel>();
            var paydays = 0;

            foreach (var s in streams)
            {
                var candidates = deposits.Where(d => d.MerchantKey == s.MerchantKey).ToList();
                // Paydays just outside the month claim their own deposits first, so a deposit near the 1st isn't counted twice
                foreach (var payday in PaySchedule.Paydays(s.Frequency, s.Anchor, first.AddDays(-14), last.AddDays(14)))
                {
                    var match = candidates
                        .Where(d => !used.Contains(d.Id) && Math.Abs(d.Date.DayNumber - payday.DayNumber) <= MatchWindowDays)
                        .OrderBy(d => Math.Abs(d.Date.DayNumber - payday.DayNumber))
                        .FirstOrDefault();
                    if (match != null) used.Add(match.Id);
                    if (payday < first || payday > last) continue;

                    paydays++;
                    if (match != null)
                    {
                        paychecks.Add(new(payday, match.Date, s.Name, match.Amount, PaycheckStatus.Received, match.AccountName, s.Id));
                    }
                    else
                    {
                        var late = payday.AddDays(MatchWindowDays) < today;
                        paychecks.Add(new(payday, null, s.Name, s.ExpectedAmount, late ? PaycheckStatus.Late : PaycheckStatus.Expected, null, s.Id));
                    }
                }
            }

            foreach (var d in deposits.Where(d => !used.Contains(d.Id) && d.Date >= first && d.Date <= last))
            {
                paychecks.Add(new(d.Date, d.Date, d.Name, d.Amount, PaycheckStatus.Received, d.AccountName, null));
            }

            paychecks = paychecks.OrderBy(p => p.Payday).ThenBy(p => p.Name).ToList();
            var received = paychecks.Where(p => p.Status == PaycheckStatus.Received).Sum(p => p.Amount);
            var expected = paychecks.Where(p => p.Status == PaycheckStatus.Expected).Sum(p => p.Amount);
            var extra = streams
                .Select(s => PaySchedule.NextExtraPaycheckMonth(s.Frequency, s.Anchor, year, month))
                .Where(d => d != null)
                .Min();

            return new IncomeModel(
                Total: incomeOverride ?? received + expected,
                Received: received,
                Expected: expected,
                Override: incomeOverride,
                Paychecks: paychecks,
                Paydays: paydays,
                NextExtraPaycheckMonth: extra?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Streams: streams.Count);
        }
    }

    /// <summary>Spots paychecks: the same payer at a steady rhythm (median gap), with biweekly told apart by 3-paycheck months.</summary>
    public static class IncomeDetector
    {
        public static List<IncomeStreamModel> Detect(IEnumerable<Deposit> deposits, DateOnly today)
        {
            var found = new List<IncomeStreamModel>();
            foreach (var g in deposits.Where(d => d.MerchantKey != "").GroupBy(d => d.MerchantKey))
            {
                // A paycheck split between two accounts on the same day is one paycheck
                var days = g.GroupBy(d => d.Date)
                    .Select(d => (Date: d.Key, Amount: d.Sum(x => x.Amount), Name: d.First().Name))
                    .OrderBy(d => d.Date)
                    .ToList();
                if (days.Count < 3) continue;

                var gaps = days.Zip(days.Skip(1), (a, b) => b.Date.DayNumber - a.Date.DayNumber).ToList();
                var gap = Stats.Median(gaps);
                PayFrequency? frequency = gap switch
                {
                    >= 5 and <= 9 => PayFrequency.Weekly,
                    >= 12 and <= 17 => PayFrequency.Biweekly,
                    >= 26 and <= 35 => PayFrequency.Monthly,
                    _ => null
                };
                if (frequency == null) continue;
                if (gaps.Count(x => Math.Abs(x - gap) <= Math.Max(3, gap * 0.25)) < gaps.Count * 0.7) continue;

                if (frequency == PayFrequency.Biweekly)
                {
                    var threeInAMonth = days.GroupBy(d => (d.Date.Year, d.Date.Month)).Any(m => m.Count() >= 3);
                    var midAndEnd = days.All(d => d.Date.Day is >= 12 and <= 16 || d.Date.Day >= 26);
                    if (!threeInAMonth && midAndEnd) frequency = PayFrequency.Semimonthly;
                }

                var last = days[^1];
                // A payer that stopped (old job) isn't proposed
                if (today.DayNumber - last.Date.DayNumber > gap * 2 + 7) continue;
                var amount = Math.Round(Stats.Median(days.TakeLast(6).Select(d => d.Amount)), 2);
                if (amount < 50) continue;

                found.Add(new IncomeStreamModel
                {
                    Name = Stats.Title(last.Name),
                    MerchantKey = g.Key,
                    Frequency = frequency.Value,
                    AnchorDate = last.Date,
                    ExpectedAmount = amount,
                    Deposits = days.Count,
                    NextPayday = PaySchedule.Paydays(frequency.Value, last.Date, today, today.AddDays(40)).Select(d => (DateOnly?)d).FirstOrDefault()
                });
            }
            return found.OrderByDescending(s => s.ExpectedAmount).ToList();
        }
    }

    /// <summary>Spots bills and subscriptions: the same merchant at a steady rhythm and a similar amount.</summary>
    public static class RecurringDetector
    {
        private static readonly (RecurringCadence Cadence, int Min, int Max, double Nominal)[] Cadences =
        [
            (RecurringCadence.Weekly, 6, 8, 7),
            (RecurringCadence.Biweekly, 13, 16, 14),
            (RecurringCadence.Monthly, 26, 35, 30.4),
            (RecurringCadence.Quarterly, 85, 97, 91),
            (RecurringCadence.Yearly, 350, 380, 365),
        ];

        public static List<RecurringModel> Detect(IEnumerable<Charge> charges, DateOnly today)
        {
            var found = new List<RecurringModel>();
            foreach (var g in charges.Where(c => c.MerchantKey != "" && c.Amount > 0).GroupBy(c => c.MerchantKey))
            {
                var days = g.GroupBy(c => c.Date)
                    .Select(d => (Date: d.Key, Amount: d.Sum(x => x.Amount), Sample: d.First()))
                    .OrderBy(d => d.Date)
                    .ToList();
                if (days.Count < 2) continue;

                var gaps = days.Zip(days.Skip(1), (a, b) => b.Date.DayNumber - a.Date.DayNumber).ToList();
                var gap = Stats.Median(gaps);
                var match = Cadences.FirstOrDefault(c => gap >= c.Min && gap <= c.Max);
                if (match.Nominal == 0) continue;
                if (days.Count < 3 && match.Cadence != RecurringCadence.Yearly) continue;
                var tolerance = Math.Max(2, match.Nominal * 0.2);
                if (gaps.Count(x => Math.Abs(x - match.Nominal) <= tolerance) < gaps.Count * 0.75) continue;

                var amounts = days.Select(d => d.Amount).ToList();
                var typical = Stats.Median(amounts.TakeLast(6));
                // Wildly different amounts are a habit (a coffee shop), not a bill
                if (amounts.TakeLast(6).Any(a => a < typical * 0.4m || a > typical * 2.5m)) continue;

                var earlier = amounts.SkipLast(1).TakeLast(5).ToList();
                var steady = earlier.Max() - earlier.Min() <= Math.Max(0.5m, typical * 0.02m);
                var lastAmount = amounts[^1];
                decimal? priceChange = steady && Math.Abs(lastAmount - amounts[^2]) >= 0.5m ? lastAmount - amounts[^2] : null;
                var variable = !steady;

                var last = days[^1];
                var dayOfMonth = (int)Math.Round(Stats.Median(days.TakeLast(3).Select(d => d.Date.Day)));
                var next = Advance(last.Date, match.Cadence, dayOfMonth);
                // Charges that stopped (a cancelled subscription) drop off once they're well overdue
                if (next.DayNumber < today.DayNumber - match.Nominal * 0.35) continue;

                var s = last.Sample;
                found.Add(new RecurringModel(
                    MerchantKey: g.Key,
                    Name: s.Name,
                    Cadence: match.Cadence,
                    Amount: variable ? Math.Round(typical, 2) : lastAmount,
                    IsVariable: variable,
                    PriceChange: priceChange,
                    LastDate: last.Date,
                    NextDate: next,
                    CategoryId: s.CategoryId,
                    CategoryName: s.CategoryName,
                    Counted: s.Counted,
                    Treatment: s.Treatment,
                    Occurrences: days.Count));
            }
            return found.OrderBy(r => r.NextDate).ThenBy(r => r.Name).ToList();
        }

        public static DateOnly Advance(DateOnly date, RecurringCadence cadence, int dayOfMonth) => cadence switch
        {
            RecurringCadence.Weekly => date.AddDays(7),
            RecurringCadence.Biweekly => date.AddDays(14),
            RecurringCadence.Quarterly => SameDay(date.AddMonths(3), dayOfMonth),
            RecurringCadence.Yearly => date.AddYears(1),
            _ => SameDay(date.AddMonths(1), dayOfMonth)
        };

        private static DateOnly SameDay(DateOnly month, int day) =>
            new(month.Year, month.Month, Math.Min(Math.Max(day, 1), DateTime.DaysInMonth(month.Year, month.Month)));

        /// <summary>Every expected charge between two dates (a weekly one can land several times)</summary>
        public static List<UpcomingChargeModel> Upcoming(IEnumerable<RecurringModel> recurring, DateOnly from, DateOnly to)
        {
            var list = new List<UpcomingChargeModel>();
            foreach (var r in recurring)
            {
                var day = r.NextDate.Day;
                for (var d = r.NextDate; d <= to; d = Advance(d, r.Cadence, day))
                {
                    if (d >= from) list.Add(new(d, r.Name, r.Amount, r.IsVariable, r.CategoryId, r.CategoryName, r.Counted, r.Treatment));
                }
            }
            return list.OrderBy(u => u.Date).ThenBy(u => u.Name).ToList();
        }
    }

    /// <summary>The month's numbers: spending against targets with pace, income, safe to spend, and what isn't assigned (plan §4.4).</summary>
    public static class BudgetCalculator
    {
        public record Input(
            int Year,
            int Month,
            DateOnly Today,
            bool HasBudget,
            IReadOnlyList<CategoryInfo> Categories,
            IReadOnlyList<TargetInput> Targets,
            IReadOnlyList<SpendLine> Spending,
            IncomeModel Income,
            decimal CommittedDebt,
            decimal CommittedGoals,
            IReadOnlyList<UpcomingChargeModel> Upcoming,
            NotCountedModel NotCounted);

        public static BudgetMonthModel Calculate(Input input)
        {
            var first = new DateOnly(input.Year, input.Month, 1);
            var last = Stats.LastDay(input.Year, input.Month);
            var daysInMonth = last.Day;
            var (day, daysLeft) = input.Today < first ? (0, daysInMonth)
                : input.Today > last ? (daysInMonth, 0)
                : (input.Today.Day, daysInMonth - input.Today.Day + 1);
            var isCurrent = day > 0 && daysLeft > 0;

            var categories = input.Categories.ToDictionary(c => c.Id);
            var spentBy = input.Spending.Where(s => s.CategoryId != null).GroupBy(s => s.CategoryId!.Value)
                .ToDictionary(g => g.Key, g => (Spent: g.Sum(s => s.Amount), Pending: g.Where(s => s.Pending).Sum(s => s.Amount)));
            var targets = input.Targets.ToDictionary(t => t.CategoryId);
            // Charges still to come this month, by category, for "will end over" and "due on"
            var remaining = input.Upcoming.Where(u => u.Counted && u.CategoryId != null && u.Date >= input.Today && u.Date <= last).ToList();

            var lines = new List<BudgetLineModel>();
            foreach (var id in targets.Keys.Union(spentBy.Keys))
            {
                if (!categories.TryGetValue(id, out var c)) continue;
                targets.TryGetValue(id, out var t);
                spentBy.TryGetValue(id, out var s);
                var available = (t?.Amount ?? 0) + (t?.Carry ?? 0);
                var flexible = c.Group == CategoryGroup.Flexible;
                decimal? expected = flexible && t != null ? Math.Round(available * day / daysInMonth, 2) : null;

                PaceStatus status;
                if (t == null) status = PaceStatus.Unbudgeted;
                else if (s.Spent > available + 0.005m) status = PaceStatus.OverBudget;
                else if (flexible && day < daysInMonth && s.Spent > expected + available * 0.10m) status = PaceStatus.OverPace;
                else if (c.Group == CategoryGroup.Fixed && available > 0 && s.Spent >= available - 0.005m) status = PaceStatus.Paid;
                else status = PaceStatus.OnTrack;

                var still = remaining.Where(u => u.CategoryId == id).ToList();
                var projected = s.Spent + still.Sum(u => u.Amount);
                decimal? projectedOver = t != null && status != PaceStatus.OverBudget && projected > available + 0.005m
                    ? projected - available : null;

                lines.Add(new BudgetLineModel(
                    CategoryId: id,
                    Name: c.Name,
                    Icon: c.Icon,
                    Group: c.Group,
                    Target: t?.Amount ?? 0,
                    Carry: t?.Carry ?? 0,
                    Available: available,
                    Rollover: t?.Rollover ?? false,
                    Spent: s.Spent,
                    Pending: s.Pending,
                    Left: available - s.Spent,
                    ExpectedByNow: expected,
                    Status: status,
                    ProjectedOver: projectedOver,
                    NextCharge: still.FirstOrDefault()));
            }
            lines = lines
                .OrderBy(l => l.Group)
                .ThenBy(l => categories[l.CategoryId].SortOrder)
                .ThenBy(l => l.Name)
                .ToList();

            var groups = Enum.GetValues<CategoryGroup>()
                .Select(g => new BudgetGroupModel(g, lines.Where(l => l.Group == g).Sum(l => l.Spent), lines.Where(l => l.Group == g).Sum(l => l.Available)))
                .ToList();

            var uncategorized = input.Spending.Where(s => s.CategoryId == null).ToList();
            var example = uncategorized.OrderByDescending(s => s.Amount).FirstOrDefault();

            var spent = input.Spending.Sum(s => s.Amount);
            var assigned = lines.Sum(l => l.Available);
            var committed = input.CommittedDebt + input.CommittedGoals;
            var safe = input.Income.Total - committed - spent;

            return new BudgetMonthModel(
                Year: input.Year,
                Month: input.Month,
                IsCurrent: isCurrent,
                DayOfMonth: day,
                DaysInMonth: daysInMonth,
                DaysLeft: daysLeft,
                HasBudget: input.HasBudget,
                Income: input.Income,
                CommittedDebt: input.CommittedDebt,
                CommittedGoals: input.CommittedGoals,
                Committed: committed,
                Spent: spent,
                PendingSpent: input.Spending.Where(s => s.Pending).Sum(s => s.Amount),
                Assigned: assigned,
                LeftInBudget: assigned - spent,
                SafeToSpend: safe,
                SafePerDay: daysLeft > 0 ? Math.Round(Math.Max(0, safe) / daysLeft, 2) : null,
                Unassigned: input.Income.Total - committed - assigned,
                Lines: lines,
                Groups: groups,
                Uncategorized: new UncategorizedModel(uncategorized.Count, uncategorized.Sum(s => s.Amount), example?.Merchant, example?.Amount),
                NotCounted: input.NotCounted,
                Upcoming: input.Upcoming.Where(u => u.Date >= input.Today).ToList(),
                Alerts: Alerts(lines, uncategorized.Count, isCurrent));
        }

        private static List<BudgetAlertModel> Alerts(List<BudgetLineModel> lines, int uncategorized, bool isCurrent)
        {
            var alerts = new List<BudgetAlertModel>();
            foreach (var l in lines.Where(l => l.Status == PaceStatus.OverBudget))
            {
                alerts.Add(new("OverBudget", "error", l.CategoryId, $"{l.Name} is {Money(l.Spent - l.Available)} over its {Money(l.Available)} target."));
            }
            if (isCurrent)
            {
                foreach (var l in lines.Where(l => l.Status == PaceStatus.OverPace))
                {
                    alerts.Add(new("OverPace", "warning", l.CategoryId, $"{l.Name} is ahead of pace: {Money(l.Spent)} spent, about {Money(l.ExpectedByNow ?? 0)} expected by now."));
                }
                foreach (var l in lines.Where(l => l.ProjectedOver != null))
                {
                    alerts.Add(new("ProjectedOver", "warning", l.CategoryId, $"{l.Name} will end {Money(l.ProjectedOver!.Value)} over its {Money(l.Available)} target."));
                }
            }
            if (uncategorized > 0)
            {
                alerts.Add(new("NeedsCategory", "info", null, uncategorized == 1 ? "1 transaction needs a category." : $"{uncategorized} transactions need a category."));
            }
            return alerts;
        }

        private static string Money(decimal amount) => amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    }
}
