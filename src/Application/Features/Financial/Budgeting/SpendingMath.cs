namespace Elysian.Application.Features.Financial.Budgeting
{
    /// <summary>How even a run of monthly totals is (docs/assistant-plan.md §4, spending_trend)</summary>
    public record TrendStats(
        int Months,
        decimal Mean,
        decimal Median,
        decimal Min,
        decimal Max,
        decimal StandardDeviation,
        /// <summary>Standard deviation as a % of the mean: how far a typical month is from average</summary>
        decimal TypicalSwingPct,
        /// <summary>"steady" (under 10%), "some_variation" (10–25%), "uneven" (over 25%), or "not_enough_data"</summary>
        string Steadiness,
        /// <summary>The fitted line's change per month as a % of the mean</summary>
        decimal TrendPctPerMonth,
        /// <summary>"up", "down" or "flat" (under 2% a month either way)</summary>
        string Direction);

    public record PeriodMover(string Name, decimal First, decimal Second, decimal Change, decimal? ChangePct);

    public record SpendRow(long Id, DateOnly Date, string MerchantKey, string Merchant, decimal Amount);

    public record LargeCharge(SpendRow Row, decimal UsualAmount, int PriorCharges);

    public record PossibleDuplicate(SpendRow First, SpendRow Second);

    public record UnusualFindings(List<LargeCharge> Large, List<PossibleDuplicate> Duplicates, List<SpendRow> NewMerchants);

    /// <summary>Pure calculations behind the assistant's analysis tools; no database access, so they're easy to test.</summary>
    public static class SpendingMath
    {
        public const decimal SteadyBelowPct = 10m;
        public const decimal UnevenAbovePct = 25m;
        public const decimal FlatBelowPct = 2m;

        public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public static TrendStats Trend(IReadOnlyList<decimal> months)
        {
            if (months.Count == 0) return new TrendStats(0, 0, 0, 0, 0, 0, 0, "not_enough_data", 0, "flat");

            var mean = months.Average();
            var sorted = months.OrderBy(v => v).ToList();
            var median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            var variance = months.Sum(v => (v - mean) * (v - mean)) / months.Count;
            var sd = (decimal)Math.Sqrt((double)variance);
            var swing = mean > 0 ? sd / mean * 100 : 0;

            // Least-squares slope over month index 0..n-1
            decimal slope = 0;
            if (months.Count >= 2)
            {
                var xMean = (months.Count - 1) / 2m;
                var num = 0m;
                var den = 0m;
                for (var i = 0; i < months.Count; i++)
                {
                    num += (i - xMean) * (months[i] - mean);
                    den += (i - xMean) * (i - xMean);
                }
                slope = den == 0 ? 0 : num / den;
            }
            var trendPct = mean > 0 ? slope / mean * 100 : 0;

            var steadiness = months.Count < 3 ? "not_enough_data"
                : swing < SteadyBelowPct ? "steady"
                : swing <= UnevenAbovePct ? "some_variation"
                : "uneven";
            var direction = months.Count < 3 || Math.Abs(trendPct) < FlatBelowPct ? "flat" : trendPct > 0 ? "up" : "down";

            return new TrendStats(months.Count, Round(mean), Round(median), Round(sorted[0]), Round(sorted[^1]), Round(sd),
                Math.Round(swing, 1), steadiness, Math.Round(trendPct, 1), direction);
        }

        /// <summary>A % change, or null when the first amount is zero</summary>
        public static decimal? ChangePct(decimal first, decimal second) =>
            first == 0 ? null : Math.Round((second - first) / Math.Abs(first) * 100, 1);

        /// <summary>The groups that changed most between two periods, biggest change first</summary>
        public static List<PeriodMover> Movers(IReadOnlyDictionary<string, decimal> first, IReadOnlyDictionary<string, decimal> second, int top)
        {
            return first.Keys.Union(second.Keys)
                .Select(k =>
                {
                    var a = first.GetValueOrDefault(k);
                    var b = second.GetValueOrDefault(k);
                    return new PeriodMover(k, Round(a), Round(b), Round(b - a), ChangePct(a, b));
                })
                .Where(m => m.Change != 0)
                .OrderByDescending(m => Math.Abs(m.Change)).ThenBy(m => m.Name)
                .Take(top)
                .ToList();
        }

        /// <summary>
        /// Charges in <paramref name="window"/> worth a second look, judged against <paramref name="history"/> (the months
        /// before it): well above the merchant's usual, the same merchant and amount twice within 3 days, and merchants
        /// never seen before. Only charges (positive amounts) are looked at.
        /// </summary>
        public static UnusualFindings FindUnusual(IReadOnlyList<SpendRow> history, IReadOnlyList<SpendRow> window)
        {
            var charges = window.Where(r => r.Amount > 0).OrderBy(r => r.Date).ThenBy(r => r.Id).ToList();
            var past = history.Where(r => r.Amount > 0).GroupBy(r => r.MerchantKey).ToDictionary(g => g.Key, g => g.Select(r => r.Amount).ToList());

            var large = new List<LargeCharge>();
            foreach (var r in charges)
            {
                if (!past.TryGetValue(r.MerchantKey, out var amounts) || amounts.Count < 3) continue;
                var usual = Median(amounts);
                if (r.Amount >= 25 && r.Amount >= usual * 2 && r.Amount - usual >= 25)
                {
                    large.Add(new LargeCharge(r, Round(usual), amounts.Count));
                }
            }

            var duplicates = new List<PossibleDuplicate>();
            foreach (var g in charges.GroupBy(r => (r.MerchantKey, r.Amount)))
            {
                var list = g.ToList();
                for (var i = 1; i < list.Count; i++)
                {
                    if (list[i].Date.DayNumber - list[i - 1].Date.DayNumber <= 3)
                    {
                        duplicates.Add(new PossibleDuplicate(list[i - 1], list[i]));
                    }
                }
            }

            var fresh = charges.Where(r => r.Amount >= 20 && !past.ContainsKey(r.MerchantKey))
                .GroupBy(r => r.MerchantKey).Select(g => g.First())
                .ToList();

            return new UnusualFindings(
                large.OrderByDescending(l => l.Row.Amount - l.UsualAmount).ToList(),
                duplicates,
                fresh.OrderByDescending(r => r.Amount).ToList());
        }

        private static decimal Median(List<decimal> values)
        {
            var s = values.OrderBy(v => v).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }

        /// <summary>How many times a year a recurring charge comes round</summary>
        public static decimal PerYear(string cadence) => cadence switch
        {
            "Weekly" => 52,
            "Biweekly" => 26,
            "Monthly" => 12,
            "Quarterly" => 4,
            "Yearly" => 1,
            _ => 12
        };
    }
}
