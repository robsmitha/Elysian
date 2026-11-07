using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Budgeting
{
    /// <summary>Savings goal math (plan §4.4). Pure: the service works out what's saved.</summary>
    public static class GoalCalculator
    {
        public record Input(decimal Target, DateOnly CycleStart, DateOnly Due, GoalCycle Cycle, decimal StartingBalance,
            decimal Saved, decimal SavedAtMonthStart, decimal ContributedThisMonth);

        public record Result(decimal ExpectedByNow, decimal RequiredMonthly, int MonthsLeft, GoalStatus Status, decimal Difference);

        private static int MonthIndex(DateOnly d) => d.Year * 12 + d.Month;

        /// <summary>Months from the given month through the due month, counting both (Nov → Mar = 5), at least 1</summary>
        public static int MonthsLeft(DateOnly month, DateOnly due) => Math.Max(1, MonthIndex(due) - MonthIndex(month) + 1);

        public static Result Calculate(Input g, DateOnly today)
        {
            var monthsLeft = MonthsLeft(today, g.Due);
            // Set aside each month: what was still needed when this month began, spread over the months left (rounded up to the cent)
            var required = today > g.Due ? 0 : Math.Ceiling(Math.Max(0, g.Target - g.SavedAtMonthStart) / monthsLeft * 100) / 100;

            // Even pace: a straight line from the starting balance to the target, one step per finished month of the cycle
            var monthsInCycle = Math.Max(1, MonthIndex(g.Due) - MonthIndex(g.CycleStart) + 1);
            var elapsed = Math.Clamp(MonthIndex(today) - MonthIndex(g.CycleStart), 0, monthsInCycle);
            var expected = Math.Round(g.StartingBalance + (g.Target - g.StartingBalance) * elapsed / monthsInCycle, 2);

            var band = g.Target * 0.05m;
            var status = g.Saved >= g.Target ? GoalStatus.Funded
                : today > g.Due ? GoalStatus.Overdue
                : g.Saved >= expected + band ? GoalStatus.Ahead
                : g.Saved < expected - band ? GoalStatus.Behind
                : GoalStatus.OnTrack;

            return new Result(expected, required, monthsLeft, status, g.Saved - expected);
        }

        /// <summary>How many months a cycle lasts; null for a one-time goal</summary>
        public static int? CycleMonths(GoalCycle cycle) => cycle switch
        {
            GoalCycle.Annual => 12,
            GoalCycle.Semiannual => 6,
            GoalCycle.Quarterly => 3,
            _ => null
        };

        /// <summary>The next cycle's due date, keeping month-end due dates at month end</summary>
        public static DateOnly NextDue(DateOnly due, int months)
        {
            var next = due.AddMonths(months);
            return due.Day == DateTime.DaysInMonth(due.Year, due.Month)
                ? new DateOnly(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month))
                : next;
        }
    }
}
