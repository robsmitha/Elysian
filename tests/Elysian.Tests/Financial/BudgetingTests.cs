using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Budgeting;
using Elysian.Application.Features.Financial.Categories;
using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using static Elysian.Tests.Financial.PlaidTestKit;

namespace Elysian.Tests.Financial
{
    public class PayScheduleTests
    {
        private static readonly DateOnly Nov6 = new(2026, 11, 6);

        [Fact]
        public void Biweekly_paydays_count_from_a_known_payday()
        {
            Assert.Equal([new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 20)], PaySchedule.InMonth(PayFrequency.Biweekly, Nov6, 2026, 11));
            Assert.Equal(3, PaySchedule.InMonth(PayFrequency.Biweekly, Nov6, 2027, 1).Count);
            // Counting backwards works too
            Assert.Equal([new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 23)], PaySchedule.InMonth(PayFrequency.Biweekly, Nov6, 2026, 10));
        }

        [Fact]
        public void The_next_three_paycheck_month_is_found() =>
            Assert.Equal(new DateOnly(2027, 1, 1), PaySchedule.NextExtraPaycheckMonth(PayFrequency.Biweekly, Nov6, 2026, 11));

        [Fact]
        public void Semimonthly_paydays_on_a_weekend_move_to_the_friday_before() =>
            // Nov 15, 2026 is a Sunday
            Assert.Equal([new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 30)], PaySchedule.InMonth(PayFrequency.Semimonthly, Nov6, 2026, 11));

        [Fact]
        public void Monthly_paydays_use_the_anchor_day_or_the_last_day() =>
            Assert.Equal([new DateOnly(2027, 2, 26)], PaySchedule.InMonth(PayFrequency.Monthly, new DateOnly(2026, 10, 30), 2027, 2));
    }

    public class IncomeTests
    {
        private static readonly StreamInfo Acme = new(1, "Acme payroll", "acme corp payroll", PayFrequency.Biweekly, new DateOnly(2026, 10, 23), 3412.18m);

        private static Deposit Pay(long id, int month, int day, decimal amount = 3412.18m, string key = "acme corp payroll") =>
            new(id, new DateOnly(2026, month, day), amount, key, "ACME CORP PAYROLL", "Adv Plus ••4410");

        [Fact]
        public void Received_and_expected_paychecks_add_up()
        {
            var income = IncomeCalculator.ForMonth(2026, 11, new DateOnly(2026, 11, 6), [Acme], [Pay(1, 10, 23), Pay(2, 11, 6, 3400.00m)], null);

            Assert.Equal(2, income.Paydays);
            Assert.Equal(3400.00m, income.Received);
            Assert.Equal(3412.18m, income.Expected);
            Assert.Equal(6812.18m, income.Total);
            Assert.Equal("2027-01", income.NextExtraPaycheckMonth);
        }

        [Fact]
        public void A_paycheck_that_lands_early_counts_in_its_payday_month()
        {
            // Paid on the 2nd; November's pay landed early, on Fri Oct 30
            var stream = Acme with { Frequency = PayFrequency.Monthly, Anchor = new DateOnly(2026, 10, 2) };
            var early = new Deposit(9, new DateOnly(2026, 10, 30), 3412.18m, "acme corp payroll", "ACME", null);

            var october = IncomeCalculator.ForMonth(2026, 10, new DateOnly(2026, 11, 6), [stream], [early], null);
            var november = IncomeCalculator.ForMonth(2026, 11, new DateOnly(2026, 11, 6), [stream], [early], null);

            Assert.DoesNotContain(october.Paychecks, p => p.ReceivedOn == early.Date);
            Assert.Contains(november.Paychecks, p => p.ReceivedOn == early.Date && p.Payday == new DateOnly(2026, 11, 2) && p.Status == PaycheckStatus.Received);
        }

        [Fact]
        public void A_missed_payday_is_not_counted()
        {
            var income = IncomeCalculator.ForMonth(2026, 11, new DateOnly(2026, 11, 25), [Acme], [Pay(2, 11, 6)], null);

            Assert.Contains(income.Paychecks, p => p.Payday == new DateOnly(2026, 11, 20) && p.Status == PaycheckStatus.Late);
            Assert.Equal(3412.18m, income.Total);
        }

        [Fact]
        public void Other_income_counts_and_an_override_replaces_everything()
        {
            var refund = Pay(5, 11, 12, 250m, "irs treas");
            var income = IncomeCalculator.ForMonth(2026, 11, new DateOnly(2026, 11, 6), [Acme], [Pay(2, 11, 6), refund], null);
            Assert.Equal(3412.18m * 2 + 250m, income.Total);

            var typed = IncomeCalculator.ForMonth(2026, 11, new DateOnly(2026, 11, 6), [Acme], [Pay(2, 11, 6)], 5000m);
            Assert.Equal(5000m, typed.Total);
        }

        [Fact]
        public void Biweekly_payroll_is_detected()
        {
            var deposits = Enumerable.Range(0, 8).Select(i => new Deposit(i, new DateOnly(2026, 7, 31).AddDays(14 * i), 3412.18m, "acme corp payroll", "ACME CORP PAYROLL", null)).ToList();

            var stream = Assert.Single(IncomeDetector.Detect(deposits, new DateOnly(2026, 11, 6)));
            Assert.Equal(PayFrequency.Biweekly, stream.Frequency);
            Assert.Equal(3412.18m, stream.ExpectedAmount);
            Assert.Equal(new DateOnly(2026, 11, 6), stream.AnchorDate);
            Assert.Equal("Acme Corp Payroll", stream.Name);
        }

        [Fact]
        public void Semimonthly_payroll_is_told_apart_from_biweekly()
        {
            var dates = new[] { (8, 14), (8, 31), (9, 15), (9, 30), (10, 15), (10, 30) };
            var deposits = dates.Select((d, i) => new Deposit(i, new DateOnly(2026, d.Item1, d.Item2), 2500m, "globex", "GLOBEX", null)).ToList();

            Assert.Equal(PayFrequency.Semimonthly, Assert.Single(IncomeDetector.Detect(deposits, new DateOnly(2026, 11, 6))).Frequency);
        }

        [Fact]
        public void Irregular_or_stopped_deposits_are_not_paychecks()
        {
            var irregular = new[] { 3, 9, 30, 41, 70 }.Select((d, i) => new Deposit(i, new DateOnly(2026, 7, 1).AddDays(d), 200m, "venmo", "VENMO", null));
            var stopped = Enumerable.Range(0, 6).Select(i => new Deposit(10 + i, new DateOnly(2026, 1, 2).AddDays(14 * i), 3000m, "old job", "OLD JOB", null));

            Assert.Empty(IncomeDetector.Detect(irregular.Concat(stopped), new DateOnly(2026, 11, 6)));
        }
    }

    public class RecurringTests
    {
        private static readonly DateOnly Today = new(2026, 11, 6);

        private static IEnumerable<Charge> Monthly(string name, int day, params decimal[] amounts) =>
            amounts.Select((a, i) => new Charge(new DateOnly(2026, 11 - amounts.Length + 1 + i, day), a, CategoryCatalog.MerchantKey(name), name, 5, "Subscriptions", Treatment.Spending, true));

        [Fact]
        public void Subscriptions_and_bills_are_found_with_their_next_date()
        {
            var found = RecurringDetector.Detect(Monthly("Netflix", 4, 22.99m, 22.99m, 22.99m, 22.99m), Today);

            var netflix = Assert.Single(found);
            Assert.Equal(RecurringCadence.Monthly, netflix.Cadence);
            Assert.Equal(new DateOnly(2026, 12, 4), netflix.NextDate);
            Assert.Equal(22.99m, netflix.Amount);
            Assert.False(netflix.IsVariable);
            Assert.Null(netflix.PriceChange);
        }

        [Fact]
        public void A_price_change_is_flagged()
        {
            var spotify = Assert.Single(RecurringDetector.Detect(Monthly("Spotify", 2, 10.99m, 10.99m, 10.99m, 11.99m), Today));
            Assert.Equal(1.00m, spotify.PriceChange);
            Assert.Equal(11.99m, spotify.Amount);
        }

        [Fact]
        public void A_bill_that_varies_uses_its_typical_amount()
        {
            var water = Assert.Single(RecurringDetector.Detect(Monthly("City water", 18, 58.10m, 71.40m, 64.00m, 66.20m), Today));
            Assert.True(water.IsVariable);
            Assert.Equal(65.10m, water.Amount);
        }

        [Fact]
        public void Habits_and_cancelled_subscriptions_are_left_out()
        {
            var coffee = new[] { 1, 3, 4, 9, 15, 16, 22, 30 }.Select(d => new Charge(new DateOnly(2026, 10, d), 5.75m + d, "starbucks", "Starbucks", 2, "Dining out", Treatment.Spending, true));
            var cancelled = new[] { 5, 6, 7 }.Select(m => new Charge(new DateOnly(2026, m, 10), 15.49m, "hulu", "Hulu", 5, "Subscriptions", Treatment.Spending, true));

            Assert.Empty(RecurringDetector.Detect(coffee.Concat(cancelled), Today));
        }
    }

    /// <summary>The numbers on mockups 1 (dashboard) and 2 (budget), Thu Nov 6, 2026.</summary>
    public class BudgetCalculatorTests
    {
        private static readonly CategoryInfo[] Categories =
        [
            new(1, "Utilities", null, CategoryGroup.Fixed, 1), new(2, "Phone & internet", null, CategoryGroup.Fixed, 2), new(3, "Subscriptions", null, CategoryGroup.Fixed, 3),
            new(4, "Groceries", null, CategoryGroup.Flexible, 4), new(5, "Dining out", null, CategoryGroup.Flexible, 5), new(6, "Shopping", null, CategoryGroup.Flexible, 6),
            new(7, "Gas & transport", null, CategoryGroup.Flexible, 7), new(8, "Entertainment", null, CategoryGroup.Flexible, 8), new(9, "Home & household", null, CategoryGroup.Flexible, 9),
            new(10, "Health", null, CategoryGroup.Flexible, 10), new(11, "Pets", null, CategoryGroup.Flexible, 11), new(12, "Personal care", null, CategoryGroup.Flexible, 12),
            new(13, "Gifts", null, CategoryGroup.NonMonthly, 13), new(14, "Other", null, CategoryGroup.NonMonthly, 14),
        ];

        private static readonly (int Id, decimal Amount)[] Targets =
            [(1, 310), (2, 165), (3, 92), (4, 800), (5, 400), (6, 350), (7, 220), (8, 150), (9, 250), (10, 120), (11, 110), (12, 80), (13, 200), (14, 300)];

        private static readonly SpendLine[] Spending =
        [
            new(1, 189.40m, false, "Duke Energy"), new(2, 165.00m, false, "Xfinity"),
            new(3, 22.99m, false, "Netflix"), new(3, 11.99m, false, "Spotify"), new(3, 19.99m, false, "iCloud"),
            new(4, 86.42m, true, "Publix"), new(4, 127.95m, false, "Trader Joe's"),
            new(5, 142.80m, false, "Restaurants"),
            new(6, 91.87m, false, "Amazon"), new(6, 188.79m, false, "Target"), new(6, -32.50m, false, "Amazon refund"),
            new(7, 41.25m, true, "Shell"), new(7, 20.00m, false, "Parking"),
            new(8, 72.33m, false, "AMC"), new(9, 38.12m, false, "Amazon"),
        ];

        private static UpcomingChargeModel Upcoming(int day, string name, decimal amount, int category, bool estimate = false) =>
            new(new DateOnly(2026, 11, day), name, amount, estimate, category, null, true, Treatment.Spending);

        private static BudgetMonthModel Mockup(SpendLine[]? extra = null)
        {
            var income = IncomeCalculator.ForMonth(2026, 11, new DateOnly(2026, 11, 6),
                [new StreamInfo(1, "Acme Corp payroll", "acme", PayFrequency.Biweekly, new DateOnly(2026, 11, 6), 3412.18m)],
                [new Deposit(1, new DateOnly(2026, 11, 6), 3412.18m, "acme", "Acme Corp payroll", "BofA ••4410")], null);

            return BudgetCalculator.Calculate(new BudgetCalculator.Input(
                2026, 11, new DateOnly(2026, 11, 6), true, Categories,
                Targets.Select(t => new TargetInput(t.Id, t.Amount, false, 0)).ToList(),
                [.. Spending, .. extra ?? []],
                income, 1200m, 1100m,
                [Upcoming(17, "Planet Fitness", 24.99m, 3), Upcoming(18, "City water", 64.00m, 1, true), Upcoming(21, "YouTube Premium", 13.99m, 3)],
                new NotCountedModel(2, 3062.40m, 2, 790.00m, 1, -32.50m, 0)));
        }

        [Fact]
        public void The_dashboard_numbers_match_the_mockup()
        {
            var m = Mockup();

            Assert.Equal((6, 30, 25), (m.DayOfMonth, m.DaysInMonth, m.DaysLeft));
            Assert.Equal(6824.36m, m.Income.Total);
            Assert.Equal(2, m.Income.Paydays);
            Assert.Equal(2300.00m, m.Committed);
            Assert.Equal(1186.40m, m.Spent);
            Assert.Equal(127.67m, m.PendingSpent);
            Assert.Equal(3547.00m, m.Assigned);
            Assert.Equal(2360.60m, m.LeftInBudget);
            Assert.Equal(3337.96m, m.SafeToSpend);
            Assert.Equal(133.52m, m.SafePerDay);
        }

        [Fact]
        public void The_budget_numbers_match_the_mockup()
        {
            var m = Mockup();
            var line = m.Lines.ToDictionary(l => l.Name);

            Assert.Equal(977.36m, m.Unassigned);
            Assert.Equal((409.37m, 567m), (m.Groups[0].Spent, m.Groups[0].Available));
            Assert.Equal((777.03m, 2480m), (m.Groups[1].Spent, m.Groups[1].Available));
            Assert.Equal((0m, 500m), (m.Groups[2].Spent, m.Groups[2].Available));

            Assert.Equal(PaceStatus.OverPace, line["Shopping"].Status);
            Assert.Equal(248.16m, line["Shopping"].Spent);
            Assert.Equal(101.84m, line["Shopping"].Left);
            Assert.Equal(PaceStatus.OverPace, line["Dining out"].Status);
            Assert.Equal(PaceStatus.OnTrack, line["Groceries"].Status);
            Assert.Equal(160.00m, line["Groceries"].ExpectedByNow);
            Assert.Equal(PaceStatus.Paid, line["Phone & internet"].Status);
            Assert.Equal(PaceStatus.OnTrack, line["Utilities"].Status);
            Assert.Null(line["Utilities"].ExpectedByNow);
            Assert.Equal("City water", line["Utilities"].NextCharge?.Name);
            Assert.Equal(1.95m, line["Subscriptions"].ProjectedOver);
            Assert.Contains(m.Alerts, a => a.Message == "Subscriptions will end $1.95 over its $92.00 target.");
        }

        [Fact]
        public void Spending_with_no_category_still_counts()
        {
            var m = Mockup([new SpendLine(null, 60m, false, "Venmo")]);

            Assert.Equal(1246.40m, m.Spent);
            Assert.Equal(new UncategorizedModel(1, 60m, "Venmo", 60m), m.Uncategorized);
            Assert.Contains(m.Alerts, a => a.Kind == "NeedsCategory");
        }

        [Fact]
        public void Spending_with_no_target_shows_up_as_unbudgeted()
        {
            var m = Mockup([new SpendLine(14, 0m, false, "x")]);
            var extra = BudgetCalculator.Calculate(new BudgetCalculator.Input(2026, 11, new DateOnly(2026, 11, 6), true, Categories, [], [new SpendLine(13, 45m, false, "Etsy")],
                m.Income, 0, 0, [], m.NotCounted));

            Assert.Equal(PaceStatus.Unbudgeted, Assert.Single(extra.Lines).Status);
        }

        [Fact]
        public void A_past_month_has_no_days_left_and_no_pace()
        {
            var m = BudgetCalculator.Calculate(new BudgetCalculator.Input(2026, 10, new DateOnly(2026, 11, 6), true, Categories,
                [new TargetInput(5, 400, false, 0)], [new SpendLine(5, 390m, false, "Dining")], Mockup().Income, 0, 0, [], Mockup().NotCounted));

            Assert.Equal((31, 0), (m.DayOfMonth, m.DaysLeft));
            Assert.Null(m.SafePerDay);
            Assert.Equal(PaceStatus.OnTrack, m.Lines[0].Status);
        }
    }

    public sealed class BudgetMonthServiceTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));
        private readonly BudgetMonthService service;
        private readonly FinancialAccount checking;
        private readonly FinancialCategory dining, groceries;
        private long nextId = 1;

        public BudgetMonthServiceTests()
        {
            var item = SeedBank(host, Protector(Settings())).Item;
            checking = new FinancialAccount { PlaidAccountId = "chk", Name = "Adv Plus", Mask = "4410", Type = "depository", Subtype = "checking", InstitutionAccessItemId = item.InstitutionAccessItemId, BudgetRole = AccountBudgetRole.Spending };
            host.Context.FinancialAccounts.Add(checking);
            dining = new FinancialCategory { Name = "Dining out", UserId = Me, Group = CategoryGroup.Flexible };
            groceries = new FinancialCategory { Name = "Groceries", UserId = Me, Group = CategoryGroup.Flexible };
            host.Context.FinancialCategories.AddRange(dining, groceries);
            host.Context.SaveChanges();
            var categories = new CategoryService(host.Context, PlaidServiceTests.Mapper);
            service = new BudgetMonthService(host.Context, new TransactionClassifier(host.Context, categories, clock), clock);
        }

        public void Dispose() => host.Dispose();

        private void Spend(DateOnly date, decimal amount, FinancialCategory? category, Treatment treatment = Treatment.Spending, string name = "Shop")
        {
            host.Context.BankTransactions.Add(new BankTransaction
            {
                PlaidTransactionId = $"t{nextId++}",
                FinancialAccountId = checking.FinancialAccountId,
                Amount = amount,
                Name = name,
                MerchantName = name,
                MerchantKey = CategoryCatalog.MerchantKey(name),
                Date = date,
                BudgetDate = date,
                CategoryId = category?.FinancialCategoryId,
                CategorySource = ClassificationSource.User,
                Treatment = treatment,
                TreatmentSource = ClassificationSource.User
            });
        }

        private void Month(int year, int month, params (FinancialCategory Category, decimal Amount, bool Rollover)[] targets)
        {
            host.Context.BudgetMonths.Add(new BudgetMonth
            {
                UserId = Me,
                Year = year,
                Month = month,
                Targets = targets.Select(t => new BudgetTarget { CategoryId = t.Category.FinancialCategoryId, Amount = t.Amount, Rollover = t.Rollover }).ToList()
            });
            host.Context.SaveChanges();
        }

        [Fact]
        public async Task Opening_a_new_month_copies_the_last_one()
        {
            Month(2026, 11, (dining, 400, true), (groceries, 800, false));

            var december = await service.GetMonthAsync(Me, 2026, 12);

            Assert.True(december.HasBudget);
            Assert.Equal([400m, 800m], december.Lines.OrderBy(l => l.Target).Select(l => l.Target));
            Assert.True(december.Lines.Single(l => l.CategoryId == dining.FinancialCategoryId).Rollover);
            Assert.Equal(2, await host.Context.BudgetMonths.CountAsync());
        }

        [Fact]
        public async Task A_past_month_without_a_budget_stays_empty()
        {
            Month(2026, 11, (dining, 400, false));

            var september = await service.GetMonthAsync(Me, 2026, 9);

            Assert.False(september.HasBudget);
            Assert.Equal(1, await host.Context.BudgetMonths.CountAsync());
        }

        [Fact]
        public async Task Unspent_money_rolls_into_next_month_when_turned_on()
        {
            Month(2026, 10, (dining, 400, true), (groceries, 800, false));
            Month(2026, 11, (dining, 400, true), (groceries, 800, false));
            Spend(new DateOnly(2026, 10, 12), 350m, dining);
            Spend(new DateOnly(2026, 10, 14), 500m, groceries);
            host.Context.SaveChanges();

            var november = await service.GetMonthAsync(Me, 2026, 11);
            var line = november.Lines.ToDictionary(l => l.CategoryId);

            Assert.Equal(50m, line[dining.FinancialCategoryId].Carry);
            Assert.Equal(450m, line[dining.FinancialCategoryId].Available);
            Assert.Equal(0m, line[groceries.FinancialCategoryId].Carry);
        }

        [Fact]
        public async Task Overspending_carries_too_and_a_gap_month_starts_over()
        {
            Month(2026, 9, (dining, 400, true));
            Month(2026, 11, (dining, 400, true));
            Spend(new DateOnly(2026, 9, 12), 100m, dining);
            host.Context.SaveChanges();
            Assert.Equal(0m, (await service.GetMonthAsync(Me, 2026, 11)).Lines.Single().Carry);

            Month(2026, 10, (dining, 400, true));
            Spend(new DateOnly(2026, 10, 12), 480m, dining);
            host.Context.SaveChanges();
            // September left 300, October had 700 and spent 480 → 220
            Assert.Equal(220m, (await service.GetMonthAsync(Me, 2026, 11)).Lines.Single().Carry);
        }

        [Fact]
        public async Task Old_budgets_become_months_once()
        {
            var old = new Budget { Name = "Oct budget", UserId = Me, StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31) };
            host.Context.Budgets.Add(old);
            host.Context.SaveChanges();
            host.Context.BudgetCategories.Add(new BudgetCategory { BudgetId = old.BudgetId, FinancialCategoryId = dining.FinancialCategoryId, Estimate = 375 });
            host.Context.SaveChanges();

            var november = await service.GetMonthAsync(Me, 2026, 11);

            Assert.Equal(375m, november.Lines.Single().Target);
            await service.GetMonthAsync(Me, 2026, 11);
            Assert.Equal(2, await host.Context.BudgetMonths.CountAsync());
        }

        [Fact]
        public async Task Targets_save_and_average_of_three_months_rounds_up()
        {
            await service.SaveTargetsAsync(Me, 2026, 11, [new BudgetTargetSave(dining.FinancialCategoryId, 400, true)]);
            Spend(new DateOnly(2026, 8, 3), 300m, groceries);
            Spend(new DateOnly(2026, 9, 3), 301m, groceries);
            Spend(new DateOnly(2026, 10, 3), 300m, groceries);
            Spend(new DateOnly(2026, 10, 5), 90m, dining);
            host.Context.SaveChanges();

            await service.CopyTargetsAsync(Me, 2026, 11, null, null, averageOfLast3: true);
            var targets = await host.Context.BudgetTargets.ToDictionaryAsync(t => t.CategoryId);

            Assert.Equal(305m, targets[groceries.FinancialCategoryId].Amount);
            Assert.Equal(30m, targets[dining.FinancialCategoryId].Amount);
            Assert.True(targets[dining.FinancialCategoryId].Rollover);
        }

        [Fact]
        public async Task Someone_elses_category_cannot_get_a_target()
        {
            var theirs = new FinancialCategory { Name = "Theirs", UserId = "someone-else" };
            host.Context.FinancialCategories.Add(theirs);
            host.Context.SaveChanges();

            await Assert.ThrowsAsync<CustomValidationException>(() =>
                service.SaveTargetsAsync(Me, 2026, 11, [new BudgetTargetSave(theirs.FinancialCategoryId, 100, false)]));
        }

        [Fact]
        public async Task Paychecks_are_found_and_set_up_on_first_use()
        {
            for (var i = 0; i < 6; i++) Spend(new DateOnly(2026, 8, 28).AddDays(14 * i), -3412.18m, null, Treatment.Income, "ACME CORP PAYROLL");
            host.Context.SaveChanges();

            var november = await service.GetMonthAsync(Me, 2026, 11);

            var stream = Assert.Single(host.Context.IncomeStreams);
            Assert.Equal(PayFrequency.Biweekly, stream.Frequency);
            Assert.Equal(6824.36m, november.Income.Total);
            Assert.Equal("2027-01", november.Income.NextExtraPaycheckMonth);

            // Removing it keeps it from being suggested again
            await service.RemoveIncomeStreamAsync(Me, stream.IncomeStreamId);
            var streams = await service.GetIncomeStreamsAsync(Me);
            Assert.Empty(streams.Streams);
            Assert.Empty(streams.Proposals);
        }

        [Fact]
        public async Task Card_payments_and_transfers_are_reported_but_not_counted()
        {
            Spend(new DateOnly(2026, 11, 5), 2450m, null, Treatment.CardPayment, "AMEX AUTOPAY");
            Spend(new DateOnly(2026, 11, 3), 490m, null, Treatment.Transfer, "Ally");
            Spend(new DateOnly(2026, 11, 3), 40m, dining);
            host.Context.SaveChanges();

            var november = await service.GetMonthAsync(Me, 2026, 11);

            Assert.Equal(40m, november.Spent);
            Assert.Equal((1, 2450m, 1, 490m), (november.NotCounted.CardPayments, november.NotCounted.CardPaymentsTotal, november.NotCounted.Transfers, november.NotCounted.TransfersTotal));
        }

        [Fact]
        public async Task Merging_a_category_moves_its_targets()
        {
            Month(2026, 11, (dining, 400, false), (groceries, 800, false));
            var categories = new CategoryService(host.Context, PlaidServiceTests.Mapper);

            await categories.ArchiveCategoryAsync(Me, dining.FinancialCategoryId, groceries.FinancialCategoryId);

            var target = Assert.Single(host.Context.BudgetTargets);
            Assert.Equal((groceries.FinancialCategoryId, 1200m), (target.CategoryId, target.Amount));
        }

        [Fact]
        public async Task Notification_email_is_off_until_turned_on()
        {
            Assert.False((await service.GetNotificationSettingsAsync(Me)).EmailEnabled);
            await service.SaveNotificationSettingsAsync(Me, new NotificationSettingsModel(true, true, false, true));
            Assert.Equal(new NotificationSettingsModel(true, true, false, true), await service.GetNotificationSettingsAsync(Me));
        }
    }
}
