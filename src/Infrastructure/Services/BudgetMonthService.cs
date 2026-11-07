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
    /// <summary>Loads one person's data for a month and hands it to the pure calculators in Budgeting/BudgetMath.cs.</summary>
    public class BudgetMonthService(ElysianContext context, ITransactionClassifier classifier, TimeProvider clock) : IBudgetMonthService
    {
        /// <summary>How far back bills and subscriptions are looked for</summary>
        private const int RecurringLookbackDays = 400;
        /// <summary>How far back paychecks are looked for</summary>
        private const int IncomeLookbackDays = 180;
        /// <summary>Rollover chains are followed at most this many months back</summary>
        private const int RolloverMonths = 12;

        private IQueryable<BankTransaction> Mine(string userId) =>
            context.BankTransactions.Where(t => t.RemovedAt == null
                && t.FinancialAccount.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId));

        /// <summary>The browser's date when it's within a day of the server's (time zones); otherwise the server's UTC date.</summary>
        private DateOnly Today(DateOnly? fromClient)
        {
            var utc = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return fromClient is DateOnly d && Math.Abs(d.DayNumber - utc.DayNumber) <= 1 ? d : utc;
        }

        private static void CheckMonth(int year, int month)
        {
            if (year < 2000 || year > 2100 || month < 1 || month > 12) throw Invalid("Month", "That isn't a valid month.");
        }

        public async Task<BudgetMonthModel> GetMonthAsync(string userId, int year, int month, DateOnly? today = null, CancellationToken cancellationToken = default)
        {
            CheckMonth(year, month);
            var now = Today(today);
            await classifier.ClassifyAsync(userId, cancellationToken: cancellationToken);
            await AdoptOldBudgetsAsync(userId, cancellationToken);
            await AdoptDetectedPaychecksAsync(userId, now, cancellationToken);
            var budget = await EnsureMonthAsync(userId, year, month, now, cancellationToken);

            var first = new DateOnly(year, month, 1);
            var last = first.AddMonths(1).AddDays(-1);

            var categories = await context.FinancialCategories.Where(c => c.UserId == userId)
                .Select(c => new CategoryInfo(c.FinancialCategoryId, c.Name, c.Icon, c.Group, c.SortOrder))
                .ToListAsync(cancellationToken);

            // Spending from the oldest month a rollover could come from, through this month
            var spending = await CountedAsync(userId, first.AddMonths(-RolloverMonths), last, cancellationToken);
            var thisMonth = spending.Where(s => s.Date >= first).Select(s => s.Line).ToList();
            var carry = await CarryAsync(userId, year, month, spending, cancellationToken);
            var targets = budget?.Targets.Select(t => new TargetInput(t.CategoryId, t.Amount, t.Rollover, carry.GetValueOrDefault(t.CategoryId))).ToList() ?? [];

            var income = IncomeCalculator.ForMonth(year, month, now, await StreamsAsync(userId, cancellationToken),
                await DepositsAsync(userId, first.AddDays(-20), last.AddDays(20), cancellationToken), budget?.IncomeOverride);

            var recurring = await DetectRecurringAsync(userId, now, cancellationToken);
            var upcomingTo = last > now.AddDays(21) ? last : now.AddDays(21);
            var upcoming = RecurringDetector.Upcoming(recurring, now > first ? now : first, upcomingTo);

            return BudgetCalculator.Calculate(new BudgetCalculator.Input(
                Year: year,
                Month: month,
                Today: now,
                HasBudget: budget != null,
                Categories: categories,
                Targets: targets,
                Spending: thisMonth,
                Income: income,
                // Phase 4 (debt plan) and Phase 5 (goals) fill these in
                CommittedDebt: 0,
                CommittedGoals: 0,
                Upcoming: upcoming,
                NotCounted: await NotCountedAsync(userId, first, last, cancellationToken)));
        }

        public async Task SaveTargetsAsync(string userId, int year, int month, IReadOnlyList<BudgetTargetSave> targets, CancellationToken cancellationToken = default)
        {
            CheckMonth(year, month);
            if (targets.Any(t => t.Amount < 0 || t.Amount > 10_000_000)) throw Invalid("Amount", "Targets must be between $0 and $10,000,000.");
            if (targets.GroupBy(t => t.CategoryId).Any(g => g.Count() > 1)) throw Invalid("CategoryId", "Each category can have one target.");
            var ids = targets.Select(t => t.CategoryId).ToList();
            var mine = await context.FinancialCategories.CountAsync(c => c.UserId == userId && ids.Contains(c.FinancialCategoryId), cancellationToken);
            if (mine != ids.Count) throw Invalid("CategoryId", "That category wasn't found.");

            var budget = await GetOrCreateAsync(userId, year, month, cancellationToken);
            Replace(budget, targets);
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task CopyTargetsAsync(string userId, int year, int month, int? fromYear, int? fromMonth, bool averageOfLast3, CancellationToken cancellationToken = default)
        {
            CheckMonth(year, month);
            var budget = await GetOrCreateAsync(userId, year, month, cancellationToken);
            List<BudgetTargetSave> targets;
            if (averageOfLast3)
            {
                // Average counted spending of the 3 full months before, rounded up to the next $5; rollover choices stay
                var first = new DateOnly(year, month, 1);
                var spending = await CountedAsync(userId, first.AddMonths(-3), first.AddDays(-1), cancellationToken);
                var archived = await context.FinancialCategories.Where(c => c.UserId == userId && c.IsArchived).Select(c => c.FinancialCategoryId).ToListAsync(cancellationToken);
                targets = spending.Where(s => s.Line.CategoryId != null && !archived.Contains(s.Line.CategoryId.Value))
                    .GroupBy(s => s.Line.CategoryId!.Value)
                    .Select(g => (Id: g.Key, Average: g.Sum(s => s.Line.Amount) / 3))
                    .Where(x => x.Average > 0)
                    .Select(x => new BudgetTargetSave(x.Id, Math.Ceiling(x.Average / 5) * 5, budget.Targets.Any(t => t.CategoryId == x.Id && t.Rollover)))
                    .ToList();
            }
            else
            {
                if (fromYear is not int fy || fromMonth is not int fm) throw Invalid("FromMonth", "Choose a month to copy from.");
                CheckMonth(fy, fm);
                var source = await context.BudgetMonths.Include(b => b.Targets)
                    .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == fy && b.Month == fm, cancellationToken)
                    ?? throw Invalid("FromMonth", "There's no budget for that month.");
                targets = source.Targets.Select(t => new BudgetTargetSave(t.CategoryId, t.Amount, t.Rollover)).ToList();
            }
            Replace(budget, targets);
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task SetIncomeOverrideAsync(string userId, int year, int month, decimal? amount, CancellationToken cancellationToken = default)
        {
            CheckMonth(year, month);
            if (amount is < 0 or > 10_000_000) throw Invalid("Amount", "Income must be between $0 and $10,000,000.");
            var budget = await GetOrCreateAsync(userId, year, month, cancellationToken);
            budget.IncomeOverride = amount;
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<SpendingTrendModel>> GetTrendsAsync(string userId, int months, DateOnly? today = null, CancellationToken cancellationToken = default)
        {
            months = Math.Clamp(months, 1, 24);
            var now = Today(today);
            await classifier.ClassifyAsync(userId, cancellationToken: cancellationToken);
            var thisMonth = new DateOnly(now.Year, now.Month, 1);
            var from = thisMonth.AddMonths(-(months - 1));
            var spending = await CountedAsync(userId, from, thisMonth.AddMonths(1).AddDays(-1), cancellationToken);
            var names = await context.FinancialCategories.Where(c => c.UserId == userId)
                .ToDictionaryAsync(c => c.FinancialCategoryId, c => c.Name, cancellationToken);

            var trends = new List<SpendingTrendModel>();
            for (var m = from; m <= thisMonth; m = m.AddMonths(1))
            {
                var inMonth = spending.Where(s => s.Date.Year == m.Year && s.Date.Month == m.Month).Select(s => s.Line).ToList();
                var byCategory = inMonth.GroupBy(s => s.CategoryId ?? 0)
                    .Select(g => new TrendCategoryModel(g.Key, g.Key == 0 ? "Needs a category" : names.GetValueOrDefault(g.Key, "Other"), g.Sum(s => s.Amount)))
                    .OrderByDescending(c => c.Amount)
                    .ToList();
                trends.Add(new SpendingTrendModel(m.Year, m.Month, inMonth.Sum(s => s.Amount), m == thisMonth, byCategory));
            }
            return trends;
        }

        public async Task<List<RecurringModel>> GetRecurringAsync(string userId, DateOnly? today = null, CancellationToken cancellationToken = default)
        {
            await classifier.ClassifyAsync(userId, cancellationToken: cancellationToken);
            return await DetectRecurringAsync(userId, Today(today), cancellationToken);
        }

        public async Task<IncomeStreamsModel> GetIncomeStreamsAsync(string userId, CancellationToken cancellationToken = default)
        {
            var now = Today(null);
            await classifier.ClassifyAsync(userId, cancellationToken: cancellationToken);
            var rows = await context.IncomeStreams.Where(s => s.UserId == userId).ToListAsync(cancellationToken);
            var known = rows.Select(s => s.MerchantKey).ToHashSet();
            var proposals = IncomeDetector.Detect(await DepositsAsync(userId, now.AddDays(-IncomeLookbackDays), now, cancellationToken), now)
                .Where(p => !known.Contains(p.MerchantKey))
                .ToList();
            return new IncomeStreamsModel(rows.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => ToModel(s, now)).ToList(), proposals);
        }

        public async Task<IncomeStreamModel> SaveIncomeStreamAsync(string userId, IncomeStreamModel model, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(model.Name) || model.Name.Length > 100) throw Invalid("Name", "Give the paycheck a name (up to 100 characters).");
            if (model.ExpectedAmount <= 0 || model.ExpectedAmount > 10_000_000) throw Invalid("ExpectedAmount", "Enter the usual take-home amount.");

            IncomeStream stream;
            if (model.IncomeStreamId > 0)
            {
                stream = await context.IncomeStreams.FirstOrDefaultAsync(s => s.IncomeStreamId == model.IncomeStreamId && s.UserId == userId, cancellationToken)
                    ?? throw Invalid("IncomeStreamId", "That paycheck wasn't found.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(model.MerchantKey)) throw Invalid("MerchantKey", "Choose who pays it.");
                // Adding one back that was removed reuses its row
                stream = await context.IncomeStreams.FirstOrDefaultAsync(s => s.UserId == userId && s.MerchantKey == model.MerchantKey, cancellationToken)
                    ?? context.IncomeStreams.Add(new IncomeStream { UserId = userId, MerchantKey = model.MerchantKey }).Entity;
            }
            stream.Name = model.Name.Trim();
            stream.Frequency = model.Frequency;
            stream.AnchorDate = model.AnchorDate;
            stream.ExpectedAmount = Math.Round(model.ExpectedAmount, 2);
            stream.IsActive = true;
            await context.SaveChangesAsync(cancellationToken);
            return ToModel(stream, Today(null));
        }

        public async Task RemoveIncomeStreamAsync(string userId, int incomeStreamId, CancellationToken cancellationToken = default)
        {
            var stream = await context.IncomeStreams.FirstOrDefaultAsync(s => s.IncomeStreamId == incomeStreamId && s.UserId == userId, cancellationToken)
                ?? throw Invalid("IncomeStreamId", "That paycheck wasn't found.");
            stream.IsActive = false;
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<NotificationSettingsModel> GetNotificationSettingsAsync(string userId, CancellationToken cancellationToken = default)
        {
            var s = await context.NotificationSettings.FirstOrDefaultAsync(n => n.UserId == userId, cancellationToken) ?? new NotificationSettings();
            return new NotificationSettingsModel(s.EmailEnabled, s.BankAlerts, s.BudgetAlerts, s.GoalAlerts);
        }

        public async Task<NotificationSettingsModel> SaveNotificationSettingsAsync(string userId, NotificationSettingsModel model, CancellationToken cancellationToken = default)
        {
            var s = await context.NotificationSettings.FirstOrDefaultAsync(n => n.UserId == userId, cancellationToken)
                ?? context.NotificationSettings.Add(new NotificationSettings { UserId = userId }).Entity;
            s.EmailEnabled = model.EmailEnabled;
            s.BankAlerts = model.BankAlerts;
            s.BudgetAlerts = model.BudgetAlerts;
            s.GoalAlerts = model.GoalAlerts;
            await context.SaveChangesAsync(cancellationToken);
            return model;
        }

        // ---- months and targets ----

        private static void Replace(BudgetMonth budget, IReadOnlyList<BudgetTargetSave> targets)
        {
            var keep = targets.Where(t => t.Amount > 0 || t.Rollover).ToDictionary(t => t.CategoryId);
            foreach (var t in budget.Targets.ToList())
            {
                if (keep.Remove(t.CategoryId, out var save))
                {
                    t.Amount = Math.Round(save.Amount, 2);
                    t.Rollover = save.Rollover;
                }
                else
                {
                    budget.Targets.Remove(t);
                }
            }
            foreach (var save in keep.Values)
            {
                budget.Targets.Add(new BudgetTarget { CategoryId = save.CategoryId, Amount = Math.Round(save.Amount, 2), Rollover = save.Rollover });
            }
        }

        private async Task<BudgetMonth> GetOrCreateAsync(string userId, int year, int month, CancellationToken cancellationToken)
        {
            var budget = await context.BudgetMonths.Include(b => b.Targets)
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == year && b.Month == month, cancellationToken);
            if (budget != null) return budget;
            budget = new BudgetMonth { UserId = userId, Year = year, Month = month };
            context.BudgetMonths.Add(budget);
            return budget;
        }

        /// <summary>
        /// The saved month, or for this month and up to a year ahead, a new one with the latest earlier month's targets.
        /// Past months with no budget stay empty (nothing is invented after the fact).
        /// </summary>
        private async Task<BudgetMonth?> EnsureMonthAsync(string userId, int year, int month, DateOnly now, CancellationToken cancellationToken)
        {
            var budget = await context.BudgetMonths.Include(b => b.Targets)
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == year && b.Month == month, cancellationToken);
            if (budget != null) return budget;

            var key = year * 12 + month;
            var nowKey = now.Year * 12 + now.Month;
            if (key < nowKey || key > nowKey + 12) return null;

            var previous = await context.BudgetMonths.Include(b => b.Targets)
                .Where(b => b.UserId == userId && b.Year * 12 + b.Month < key)
                .OrderByDescending(b => b.Year).ThenByDescending(b => b.Month)
                .FirstOrDefaultAsync(cancellationToken);
            if (previous == null) return null;

            budget = new BudgetMonth
            {
                UserId = userId,
                Year = year,
                Month = month,
                Targets = previous.Targets.Select(t => new BudgetTarget { CategoryId = t.CategoryId, Amount = t.Amount, Rollover = t.Rollover }).ToList()
            };
            context.BudgetMonths.Add(budget);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another page load made it first
                context.ChangeTracker.Clear();
                budget = await context.BudgetMonths.Include(b => b.Targets)
                    .FirstAsync(b => b.UserId == userId && b.Year == year && b.Month == month, cancellationToken);
            }
            return budget;
        }

        /// <summary>
        /// Once, before a person's first monthly budget: each old budget becomes the month it started in (the latest one
        /// wins when several start in the same month), with its estimates as targets.
        /// </summary>
        private async Task AdoptOldBudgetsAsync(string userId, CancellationToken cancellationToken)
        {
            if (await context.BudgetMonths.AnyAsync(b => b.UserId == userId, cancellationToken)) return;
            var lines = await context.BudgetCategories
                .Where(l => l.Budget.UserId == userId && l.FinancialCategory.UserId == userId && l.Estimate > 0)
                .Select(l => new { l.BudgetId, l.Budget.StartDate, l.FinancialCategoryId, l.Estimate })
                .ToListAsync(cancellationToken);
            if (lines.Count == 0) return;

            foreach (var month in lines.GroupBy(l => (l.StartDate.Year, l.StartDate.Month)))
            {
                var latest = month.OrderByDescending(l => l.StartDate).ThenByDescending(l => l.BudgetId).First().BudgetId;
                context.BudgetMonths.Add(new BudgetMonth
                {
                    UserId = userId,
                    Year = month.Key.Year,
                    Month = month.Key.Month,
                    Targets = month.Where(l => l.BudgetId == latest)
                        .GroupBy(l => l.FinancialCategoryId)
                        .Select(g => new BudgetTarget { CategoryId = g.Key, Amount = g.Sum(l => l.Estimate) })
                        .ToList()
                });
            }
            await context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>When someone has no paychecks set up at all, steady ones found in their deposits are added (they can remove them).</summary>
        private async Task AdoptDetectedPaychecksAsync(string userId, DateOnly now, CancellationToken cancellationToken)
        {
            if (await context.IncomeStreams.AnyAsync(s => s.UserId == userId, cancellationToken)) return;
            var found = IncomeDetector.Detect(await DepositsAsync(userId, now.AddDays(-IncomeLookbackDays), now, cancellationToken), now)
                .Where(p => p.Deposits >= 4)
                .ToList();
            foreach (var p in found)
            {
                context.IncomeStreams.Add(new IncomeStream
                {
                    UserId = userId,
                    Name = p.Name,
                    MerchantKey = p.MerchantKey,
                    Frequency = p.Frequency,
                    AnchorDate = p.AnchorDate,
                    ExpectedAmount = p.ExpectedAmount
                });
            }
            if (found.Count > 0) await context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// What carries into this month per category: walking forward through consecutive saved months, a target with
        /// rollover on passes on (target + what it carried in − spent). A gap in saved months starts over.
        /// </summary>
        private async Task<Dictionary<int, decimal>> CarryAsync(string userId, int year, int month, List<(DateOnly Date, SpendLine Line)> spending, CancellationToken cancellationToken)
        {
            var key = year * 12 + month;
            var months = await context.BudgetMonths.Include(b => b.Targets)
                .Where(b => b.UserId == userId && b.Year * 12 + b.Month < key && b.Year * 12 + b.Month >= key - RolloverMonths)
                .OrderBy(b => b.Year).ThenBy(b => b.Month)
                .ToListAsync(cancellationToken);

            var carry = new Dictionary<int, decimal>();
            var expect = -1;
            foreach (var m in months)
            {
                var k = m.Year * 12 + m.Month;
                if (k != expect) carry.Clear();
                var spent = spending.Where(s => s.Date.Year == m.Year && s.Date.Month == m.Month && s.Line.CategoryId != null)
                    .GroupBy(s => s.Line.CategoryId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(s => s.Line.Amount));
                carry = m.Targets.Where(t => t.Rollover)
                    .ToDictionary(t => t.CategoryId, t => t.Amount + carry.GetValueOrDefault(t.CategoryId) - spent.GetValueOrDefault(t.CategoryId));
                expect = k + 1;
            }
            return expect == key ? carry : [];
        }

        // ---- transaction reads ----

        /// <summary>Counted spending by date: one line per transaction, or per split part. Refunds are negative.</summary>
        private async Task<List<(DateOnly Date, SpendLine Line)>> CountedAsync(string userId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            var rows = await Mine(userId)
                .Where(t => t.BudgetDate >= from && t.BudgetDate <= to
                    && t.FinancialAccount.BudgetRole == AccountBudgetRole.Spending
                    && (t.Treatment == Treatment.Spending || t.Treatment == Treatment.Refund))
                .Select(t => new
                {
                    t.BudgetDate,
                    t.CategoryId,
                    t.Amount,
                    t.Pending,
                    Merchant = t.MerchantName ?? t.Name,
                    Splits = t.Splits.Select(s => new { s.CategoryId, s.Amount }).ToList()
                })
                .ToListAsync(cancellationToken);

            var lines = new List<(DateOnly, SpendLine)>();
            foreach (var r in rows)
            {
                if (r.Splits.Count > 0)
                {
                    lines.AddRange(r.Splits.Select(s => (r.BudgetDate, new SpendLine(s.CategoryId, s.Amount, r.Pending, r.Merchant))));
                }
                else
                {
                    lines.Add((r.BudgetDate, new SpendLine(r.CategoryId, r.Amount, r.Pending, r.Merchant)));
                }
            }
            return lines;
        }

        private async Task<List<Deposit>> DepositsAsync(string userId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            await Mine(userId)
                .Where(t => t.BudgetDate >= from && t.BudgetDate <= to && t.Amount < 0 && t.Treatment == Treatment.Income
                    && (t.FinancialAccount.BudgetRole == AccountBudgetRole.Spending || t.FinancialAccount.BudgetRole == AccountBudgetRole.IncomeOnly))
                .Select(t => new Deposit(t.BankTransactionId, t.BudgetDate, -t.Amount, t.MerchantKey, t.MerchantName ?? t.Name,
                    t.FinancialAccount.Mask != null ? t.FinancialAccount.Name + " ••" + t.FinancialAccount.Mask : t.FinancialAccount.Name))
                .ToListAsync(cancellationToken);

        private async Task<List<StreamInfo>> StreamsAsync(string userId, CancellationToken cancellationToken) =>
            await context.IncomeStreams.Where(s => s.UserId == userId && s.IsActive)
                .Select(s => new StreamInfo(s.IncomeStreamId, s.Name, s.MerchantKey, s.Frequency, s.AnchorDate, s.ExpectedAmount))
                .ToListAsync(cancellationToken);

        private async Task<List<RecurringModel>> DetectRecurringAsync(string userId, DateOnly now, CancellationToken cancellationToken)
        {
            var from = now.AddDays(-RecurringLookbackDays);
            var charges = await Mine(userId)
                .Where(t => t.BudgetDate >= from && t.Amount > 0 && !t.Pending
                    && t.Treatment != Treatment.Hidden && t.Treatment != Treatment.Income && t.Treatment != Treatment.Refund
                    && t.FinancialAccount.BudgetRole != AccountBudgetRole.Ignore)
                .Select(t => new Charge(t.BudgetDate, t.Amount, t.MerchantKey, t.MerchantName ?? t.Name, t.CategoryId,
                    t.Category != null ? t.Category.Name : null, t.Treatment,
                    t.FinancialAccount.BudgetRole == AccountBudgetRole.Spending && t.Treatment == Treatment.Spending))
                .ToListAsync(cancellationToken);
            return RecurringDetector.Detect(charges, now);
        }

        private async Task<NotCountedModel> NotCountedAsync(string userId, DateOnly first, DateOnly last, CancellationToken cancellationToken)
        {
            var rows = await Mine(userId)
                .Where(t => t.BudgetDate >= first && t.BudgetDate <= last)
                .Select(t => new { t.Treatment, t.Amount, Counting = t.FinancialAccount.BudgetRole == AccountBudgetRole.Spending })
                .ToListAsync(cancellationToken);
            // Both sides of a card payment or transfer are stored; the money-out side is the one to report
            var payments = rows.Where(r => r.Treatment == Treatment.CardPayment && r.Amount > 0).ToList();
            var transfers = rows.Where(r => r.Treatment == Treatment.Transfer && r.Amount > 0).ToList();
            var refunds = rows.Where(r => r.Treatment == Treatment.Refund && r.Counting).ToList();
            return new NotCountedModel(
                payments.Count, payments.Sum(r => r.Amount),
                transfers.Count, transfers.Sum(r => r.Amount),
                refunds.Count, refunds.Sum(r => r.Amount),
                rows.Count(r => r.Treatment == Treatment.Hidden));
        }

        private static IncomeStreamModel ToModel(IncomeStream s, DateOnly now) => new()
        {
            IncomeStreamId = s.IncomeStreamId,
            Name = s.Name,
            MerchantKey = s.MerchantKey,
            Frequency = s.Frequency,
            AnchorDate = s.AnchorDate,
            ExpectedAmount = s.ExpectedAmount,
            IsActive = s.IsActive,
            NextPayday = PaySchedule.Paydays(s.Frequency, s.AnchorDate, now, now.AddDays(40)).Select(d => (DateOnly?)d).FirstOrDefault()
        };

        private static CustomValidationException Invalid(string field, string message) =>
            new([new FluentValidation.Results.ValidationFailure(field, message)]);
    }
}
