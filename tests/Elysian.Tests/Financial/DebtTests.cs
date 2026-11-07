using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Budgeting;
using Elysian.Application.Features.Financial.Categories;
using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using static Elysian.Tests.Financial.PlaidTestKit;

namespace Elysian.Tests.Financial
{
    public class DebtPayoffCalculatorTests
    {
        private static readonly DateOnly Nov = new(2026, 11, 1);

        [Fact]
        public void One_debt_matches_the_amortization_formula()
        {
            // $5,000 at 18% over 24 months: payment = rB / (1 − (1 + r)^−n) = $249.62
            var r = 0.015;
            var payment = Math.Round((decimal)(r * 5000 / (1 - Math.Pow(1 + r, -24))), 2);
            Assert.Equal(249.62m, payment);

            var run = DebtPayoffCalculator.Simulate([new DebtInput(1, "Card", 5000, 18, 25)], payment, DebtStrategy.Avalanche, Nov);

            Assert.Equal(24, run.Summary.Months);
            Assert.Equal(new DateOnly(2028, 10, 1), run.Summary.DebtFreeMonth);
            Assert.InRange(run.Summary.TotalInterest, 990.88m - 0.25m, 990.88m + 0.25m);
        }

        [Fact]
        public void Three_cards_first_month_to_the_cent()
        {
            // The cards on mockup 5, $1,200 a month, avalanche
            var run = DebtPayoffCalculator.Simulate(
            [
                new DebtInput(1, "AMEX", 4812.33m, 24.49m, 97),
                new DebtInput(2, "BofA", 3260.55m, 21.24m, 65),
                new DebtInput(3, "Best Buy", 1150.00m, 29.99m, 35),
            ], 1200, DebtStrategy.Avalanche, Nov);

            var first = run.Schedule[0].Debts.ToDictionary(l => l.DebtId);
            Assert.Equal((98.21m, 97m, 4813.54m), (first[1].Interest, first[1].Payment, first[1].End));
            Assert.Equal((57.71m, 65m, 3253.26m), (first[2].Interest, first[2].Payment, first[2].End));
            // Best Buy has the highest APR: its minimum plus everything left over
            Assert.Equal((28.74m, 1038m, 140.74m), (first[3].Interest, first[3].Payment, first[3].End));
            Assert.Equal(1200m, run.Schedule[0].Paid);
            Assert.Equal(new DateOnly(2026, 12, 1), run.PaidOff[3]);
            // The mockup's AMEX minimum ($97) is below its first month's interest ($98.21), which is worth knowing
            Assert.Contains("AMEX's minimum ($97.00) doesn't cover its monthly interest ($98.21).", Assert.Single(run.Warnings));
        }

        [Fact]
        public void Avalanche_and_snowball_put_the_extra_in_different_places()
        {
            DebtInput[] debts = [new(1, "Small", 1000, 10, 25), new(2, "Big", 5000, 25, 100)];

            var avalanche = DebtPayoffCalculator.Simulate(debts, 500, DebtStrategy.Avalanche, Nov).Schedule[0].Debts.ToDictionary(l => l.DebtId);
            var snowball = DebtPayoffCalculator.Simulate(debts, 500, DebtStrategy.Snowball, Nov).Schedule[0].Debts.ToDictionary(l => l.DebtId);
            var custom = DebtPayoffCalculator.Simulate(debts, 500, DebtStrategy.Custom, Nov, [1]).Schedule[0].Debts.ToDictionary(l => l.DebtId);

            Assert.Equal((25m, 475m), (avalanche[1].Payment, avalanche[2].Payment));
            Assert.Equal((400m, 100m), (snowball[1].Payment, snowball[2].Payment));
            Assert.Equal((400m, 100m), (custom[1].Payment, custom[2].Payment));
        }

        [Fact]
        public void A_promo_rate_switches_to_the_regular_rate_after_it_ends()
        {
            // 0% until Feb 15, 2027: Nov, Dec, Jan and Feb are interest-free; March is charged
            var run = DebtPayoffCalculator.Simulate([new DebtInput(1, "Transfer", 3000, 24, 100, 0, new DateOnly(2027, 2, 15))], 100, DebtStrategy.Avalanche, Nov);

            Assert.All(run.Schedule.Take(4), m => Assert.Equal(0m, m.Interest));
            Assert.Equal(52.00m, run.Schedule[4].Interest);
            Assert.DoesNotContain(run.Warnings, w => w.Contains("deferred"));
        }

        [Fact]
        public void Deferred_interest_warns_when_the_promo_balance_wont_clear()
        {
            var run = DebtPayoffCalculator.Simulate([new DebtInput(1, "Store card", 3000, 26.99m, 100, 0, new DateOnly(2027, 2, 15), DeferredInterest: true)], 100, DebtStrategy.Avalanche, Nov);
            Assert.Contains(run.Warnings, w => w.Contains("Store card will still owe $2,600.00") && w.Contains("deferred interest"));

            var cleared = DebtPayoffCalculator.Simulate([new DebtInput(1, "Store card", 3000, 26.99m, 100, 0, new DateOnly(2027, 2, 15), DeferredInterest: true)], 800, DebtStrategy.Avalanche, Nov);
            Assert.DoesNotContain(cleared.Warnings, w => w.Contains("deferred"));
        }

        [Fact]
        public void Avalanche_order_follows_the_rate_each_month()
        {
            // Promo card is 0% for two months, then 30%: the extra goes to the 20% card first, then switches
            DebtInput[] debts = [new(1, "Promo", 2000, 30, 25, 0, new DateOnly(2026, 12, 20)), new(2, "Regular", 4000, 20, 50)];
            var run = DebtPayoffCalculator.Simulate(debts, 500, DebtStrategy.Avalanche, Nov);

            Assert.Equal(25m, run.Schedule[0].Debts.Single(l => l.DebtId == 1).Payment);
            // Its $25 minimum plus the $425 left after both minimums
            Assert.Equal(450m, run.Schedule[2].Debts.Single(l => l.DebtId == 1).Payment);
        }

        [Fact]
        public void Payments_below_the_interest_never_finish()
        {
            var run = DebtPayoffCalculator.Simulate([new DebtInput(1, "Loan", 10000, 24, 150)], 150, DebtStrategy.Avalanche, Nov);

            Assert.True(run.Summary.NeverPaysOff);
            Assert.Null(run.Summary.DebtFreeMonth);
            Assert.Contains(run.Warnings, w => w.Contains("never get paid off"));
            Assert.Contains(run.Warnings, w => w.Contains("doesn't cover its monthly interest"));
        }

        [Fact]
        public void A_plan_below_the_minimums_is_flagged() =>
            Assert.Contains(DebtPayoffCalculator.Simulate([new DebtInput(1, "A", 1000, 20, 100), new DebtInput(2, "B", 1000, 20, 100)], 150, DebtStrategy.Avalanche, Nov).Warnings,
                w => w.Contains("less than the minimums ($200.00)"));
    }

    public sealed class DebtServiceTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero));
        private readonly DebtService service;
        private readonly FinancialAccount card;
        private long nextId = 1;

        public DebtServiceTests()
        {
            var item = SeedBank(host, Protector(Settings())).Item;
            card = new FinancialAccount { PlaidAccountId = "amex", Name = "Blue Cash", Mask = "2210", Type = "credit", Subtype = "credit card", CurrentBalance = 5000, InstitutionAccessItemId = item.InstitutionAccessItemId };
            host.Context.FinancialAccounts.Add(card);
            host.Context.FinancialAccounts.Add(new FinancialAccount { PlaidAccountId = "chk", Name = "Checking", Type = "depository", Subtype = "checking", CurrentBalance = 900, InstitutionAccessItemId = item.InstitutionAccessItemId });
            host.Context.SaveChanges();
            service = new DebtService(host.Context, clock);
        }

        public void Dispose() => host.Dispose();

        private void CardTransaction(DateOnly date, decimal amount, Treatment treatment)
        {
            host.Context.BankTransactions.Add(new BankTransaction
            {
                PlaidTransactionId = $"t{nextId++}",
                FinancialAccountId = card.FinancialAccountId,
                Amount = amount,
                Name = "x",
                MerchantKey = "x",
                Date = date,
                BudgetDate = date,
                Treatment = treatment,
                TreatmentSource = ClassificationSource.User,
            });
            host.Context.SaveChanges();
        }

        [Fact]
        public async Task Linked_cards_become_debts_and_hand_entered_details_survive_balance_changes()
        {
            var debt = Assert.Single((await service.GetPlanAsync(Me)).Debts);
            Assert.Equal((DebtSource.Linked, 5000m, false), (debt.Source, debt.Balance, debt.DetailsConfirmed));
            Assert.Equal("Bank of Testing ••2210", debt.Detail);

            debt.Apr = 24.49m;
            debt.MinimumPayment = 97;
            await service.SaveDebtAsync(Me, debt);
            card.CurrentBalance = 4812.33m;
            host.Context.SaveChanges();

            var after = Assert.Single((await service.GetPlanAsync(Me)).Debts);
            Assert.Equal((4812.33m, 24.49m, 97m, true), (after.Balance, after.Apr, after.MinimumPayment, after.DetailsConfirmed));
        }

        [Fact]
        public async Task Manual_debts_can_be_added_and_removed()
        {
            var added = await service.SaveDebtAsync(Me, new DebtModel { Name = "My Best Buy Visa", Balance = 1150, Apr = 29.99m, MinimumPayment = 35 });
            Assert.Equal(2, (await service.GetPlanAsync(Me)).Debts.Count);

            await service.RemoveDebtAsync(Me, added.DebtId);
            Assert.Single((await service.GetPlanAsync(Me)).Debts);
        }

        [Fact]
        public async Task A_promo_rate_needs_an_end_date() =>
            await Assert.ThrowsAsync<CustomValidationException>(() =>
                service.SaveDebtAsync(Me, new DebtModel { Name = "x", Balance = 100, Apr = 20, PromoApr = 0 }));

        [Fact]
        public async Task A_short_payment_month_shows_behind_by_the_difference()
        {
            var debt = (await service.GetPlanAsync(Me)).Debts.Single();
            debt.Apr = 24;
            debt.MinimumPayment = 100;
            await service.SaveDebtAsync(Me, debt);
            await service.SavePlanAsync(Me, new SaveDebtPlanModel(1200, DebtStrategy.Avalanche, null));

            // October: only $900 reached the card, and nothing new was spent
            CardTransaction(new DateOnly(2026, 10, 15), -900, Treatment.CardPayment);
            card.CurrentBalance = 4200;
            host.Context.SaveChanges();
            clock.Now = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);

            var plan = await service.GetPlanAsync(Me);
            var october = plan.History[0];
            var november = plan.History[1];

            Assert.Equal(("Baseline", 900m, 300m), (october.Status, october.PaymentsFound, october.PaymentShortfall));
            // Planned Nov 1: 5,000 + 100 interest − 1,200 = 3,900; actual 4,200
            Assert.Equal((3900m, 4200m, "Behind", -300m), (november.Planned, november.Actual, november.Status, november.Difference));
            Assert.Null(november.PaymentShortfall);
        }

        [Fact]
        public async Task Past_balances_are_rebuilt_from_card_transactions()
        {
            await service.SavePlanAsync(Me, new SaveDebtPlanModel(1200, DebtStrategy.Avalanche, null));
            clock.Now = new DateTimeOffset(2026, 12, 3, 15, 0, 0, TimeSpan.Zero);
            // Since Nov 1: a $1,500 payment and $200 of purchases; today's balance is $3,000
            CardTransaction(new DateOnly(2026, 11, 10), -1500, Treatment.CardPayment);
            CardTransaction(new DateOnly(2026, 11, 20), 200, Treatment.Spending);
            card.CurrentBalance = 3000;
            host.Context.SaveChanges();

            var history = (await service.GetPlanAsync(Me)).History;

            Assert.Equal(4300m, history[1].Actual);
            Assert.Equal(3000m, history[2].Actual);
            Assert.Equal(1500m, history[1].PaymentsFound);
        }

        [Fact]
        public async Task The_plan_amount_is_committed_in_the_budget()
        {
            await service.SavePlanAsync(Me, new SaveDebtPlanModel(1200, DebtStrategy.Snowball, null));
            var categories = new CategoryService(host.Context, PlaidServiceTests.Mapper);
            var budgets = new BudgetMonthService(host.Context, new TransactionClassifier(host.Context, categories, clock), clock);

            var october = await budgets.GetMonthAsync(Me, 2026, 10);
            var september = await budgets.GetMonthAsync(Me, 2026, 9);

            Assert.Equal(1200m, october.CommittedDebt);
            Assert.Equal(0m, september.CommittedDebt);
        }

        [Fact]
        public async Task A_what_if_amount_finishes_sooner()
        {
            var debt = (await service.GetPlanAsync(Me)).Debts.Single();
            debt.Apr = 24;
            debt.MinimumPayment = 100;
            await service.SaveDebtAsync(Me, debt);
            await service.SavePlanAsync(Me, new SaveDebtPlanModel(500, DebtStrategy.Avalanche, null));

            var plan = await service.GetPlanAsync(Me, extra: 250);

            Assert.True(plan.WhatIf!.Months < plan.Selected.Summary.Months);
            Assert.True(plan.WhatIf.TotalInterest < plan.Selected.Summary.TotalInterest);
        }
    }
}
