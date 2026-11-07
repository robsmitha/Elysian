using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Budgeting;
using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using static Elysian.Tests.Financial.PlaidTestKit;

namespace Elysian.Tests.Financial
{
    public class GoalCalculatorTests
    {
        private static readonly DateOnly Nov6 = new(2026, 11, 6);

        private static GoalCalculator.Result Calc(decimal target, DateOnly start, DateOnly due, decimal saved, decimal? atMonthStart = null, GoalCycle cycle = GoalCycle.Annual, DateOnly? today = null) =>
            GoalCalculator.Calculate(new GoalCalculator.Input(target, start, due, cycle, 0, saved, atMonthStart ?? saved, saved - (atMonthStart ?? saved)), today ?? Nov6);

        [Fact]
        public void Property_taxes_need_490_a_month_and_are_on_track()
        {
            // $6,000 due Mar 31 with $3,550 saved; the cycle began Apr 1
            var r = Calc(6000, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), 3550);

            Assert.Equal(5, r.MonthsLeft);
            Assert.Equal(490m, r.RequiredMonthly);
            Assert.Equal(3500m, r.ExpectedByNow);
            Assert.Equal(GoalStatus.OnTrack, r.Status);
            Assert.Equal(50m, r.Difference);
        }

        [Fact]
        public void Money_added_this_month_doesnt_shrink_this_months_amount()
        {
            // $490 went in on Nov 3; the month's amount still comes from where it stood on Nov 1
            var r = Calc(6000, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), saved: 4040, atMonthStart: 3550);
            Assert.Equal(490m, r.RequiredMonthly);
        }

        [Fact]
        public void A_goal_well_below_the_even_pace_is_behind() =>
            Assert.Equal(GoalStatus.Behind, Calc(900, new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 15), 300).Status);

        [Fact]
        public void Funded_and_overdue_goals()
        {
            Assert.Equal(GoalStatus.Funded, Calc(900, new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 15), 900).Status);
            var late = Calc(900, new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 1), 500, cycle: GoalCycle.OneTime);
            Assert.Equal((GoalStatus.Overdue, 0m), (late.Status, late.RequiredMonthly));
        }

        [Theory]
        [InlineData("2027-03-31", 12, "2028-03-31")]
        [InlineData("2027-02-28", 6, "2027-08-31")]
        [InlineData("2027-02-15", 6, "2027-08-15")]
        public void Month_end_due_dates_stay_at_month_end(string due, int months, string next) =>
            Assert.Equal(DateOnly.Parse(next), GoalCalculator.NextDue(DateOnly.Parse(due), months));
    }

    public sealed class GoalServiceTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));
        private readonly GoalService service;
        private readonly FinancialAccount checking, ally;
        private long nextId = 1;

        public GoalServiceTests()
        {
            var item = SeedBank(host, Protector(Settings())).Item;
            checking = new FinancialAccount { PlaidAccountId = "chk", Name = "Adv Plus", Mask = "4410", Type = "depository", Subtype = "checking", CurrentBalance = 2000, BudgetRole = AccountBudgetRole.Spending, InstitutionAccessItemId = item.InstitutionAccessItemId };
            ally = new FinancialAccount { PlaidAccountId = "ally", Name = "Online Savings", Mask = "9035", Type = "depository", Subtype = "savings", CurrentBalance = 16590, BudgetRole = AccountBudgetRole.Savings, InstitutionAccessItemId = item.InstitutionAccessItemId };
            host.Context.FinancialAccounts.AddRange(checking, ally);
            host.Context.SaveChanges();
            service = new GoalService(host.Context, clock);
        }

        public void Dispose() => host.Dispose();

        private BankTransaction Txn(FinancialAccount account, DateOnly date, decimal amount, Treatment treatment = Treatment.Transfer)
        {
            var t = new BankTransaction
            {
                PlaidTransactionId = $"t{nextId++}",
                FinancialAccountId = account.FinancialAccountId,
                Amount = amount,
                Name = "TRANSFER",
                MerchantKey = "transfer",
                Date = date,
                BudgetDate = date,
                Treatment = treatment,
                TreatmentSource = ClassificationSource.User,
            };
            host.Context.BankTransactions.Add(t);
            host.Context.SaveChanges();
            return t;
        }

        private Task<GoalModel> PropertyTaxes(decimal alreadySaved = 3550, GoalCycleEnd end = GoalCycleEnd.Reset) =>
            service.SaveGoalAsync(Me, new GoalModel
            {
                Name = "Property taxes",
                Icon = "mdi-home-city-outline",
                TargetAmount = 6000,
                DueDate = new DateOnly(2027, 3, 31),
                Cycle = GoalCycle.Annual,
                CycleEnd = end,
                FundingMode = GoalFundingMode.Shared,
                LinkedAccountId = ally.FinancialAccountId,
                StartingBalance = alreadySaved,
            });

        [Fact]
        public async Task A_transfer_tagged_from_both_sides_counts_once()
        {
            var goal = await PropertyTaxes();
            var outOfChecking = Txn(checking, new DateOnly(2026, 11, 3), 490);
            var intoAlly = Txn(ally, new DateOnly(2026, 11, 3), -490);
            Assert.Single((await service.GetGoalsAsync(Me)).Transfers);

            var first = await service.AddContributionAsync(Me, new AddContributionModel(goal.SavingsGoalId, null, null, intoAlly.BankTransactionId, null));
            var second = await service.AddContributionAsync(Me, new AddContributionModel(goal.SavingsGoalId, null, null, outOfChecking.BankTransactionId, null));

            Assert.Equal(first.GoalContributionId, second.GoalContributionId);
            Assert.Equal(1, await host.Context.GoalContributions.CountAsync());
            var goals = await service.GetGoalsAsync(Me);
            Assert.Equal(4040m, goals.Goals.Single().Saved);
            Assert.Equal(490m, goals.Goals.Single().ContributedThisMonth);
            Assert.Empty(goals.Transfers);
        }

        [Fact]
        public async Task A_transfer_can_only_count_toward_one_goal()
        {
            var taxes = await PropertyTaxes();
            var gifts = await service.SaveGoalAsync(Me, new GoalModel { Name = "Holiday gifts", TargetAmount = 900, DueDate = new DateOnly(2026, 12, 15), Cycle = GoalCycle.Annual });
            var t = Txn(ally, new DateOnly(2026, 11, 3), -300);

            await service.AddContributionAsync(Me, new AddContributionModel(gifts.SavingsGoalId, null, null, t.BankTransactionId, null));
            await Assert.ThrowsAsync<CustomValidationException>(() =>
                service.AddContributionAsync(Me, new AddContributionModel(taxes.SavingsGoalId, null, null, t.BankTransactionId, null)));
        }

        [Fact]
        public async Task The_goals_monthly_amounts_are_committed_in_the_budget()
        {
            await PropertyTaxes();
            var categories = new CategoryService(host.Context, PlaidServiceTests.Mapper);
            var budgets = new BudgetMonthService(host.Context, new TransactionClassifier(host.Context, categories, clock), clock);

            var november = await budgets.GetMonthAsync(Me, 2026, 11);

            Assert.Equal(490m, november.CommittedGoals);
            // December, with nothing added yet: still $2,450 to go over 4 months
            Assert.Equal(612.50m, (await budgets.GetMonthAsync(Me, 2026, 12)).CommittedGoals);
        }

        [Theory]
        [InlineData(GoalCycleEnd.Reset, 0)]
        [InlineData(GoalCycleEnd.RollOverSurplus, 100)]
        public async Task On_April_1_the_goal_starts_a_new_year(GoalCycleEnd end, int startsWith)
        {
            var goal = await PropertyTaxes(3550, end);
            await service.AddContributionAsync(Me, new AddContributionModel(goal.SavingsGoalId, 2550, new DateOnly(2027, 3, 1), null, "catch up"));

            clock.Now = new DateTimeOffset(2027, 4, 1, 15, 0, 0, TimeSpan.Zero);
            var after = (await service.GetGoalsAsync(Me)).Goals.Single();

            Assert.Equal(new DateOnly(2027, 4, 1), after.CycleStart);
            Assert.Equal(new DateOnly(2028, 3, 31), after.DueDate);
            Assert.Equal((decimal)startsWith, after.Saved);
            var past = Assert.Single(after.PastCycles);
            Assert.Equal((6000m, 6100m), (past.Target, past.Saved));
            Assert.Equal(Math.Ceiling((6000m - startsWith) / 12 * 100) / 100, after.RequiredMonthly);
        }

        [Fact]
        public async Task A_dedicated_goal_follows_its_account_balance()
        {
            var goal = await service.SaveGoalAsync(Me, new GoalModel
            {
                Name = "Emergency fund",
                TargetAmount = 20000,
                DueDate = new DateOnly(2027, 12, 31),
                Cycle = GoalCycle.OneTime,
                FundingMode = GoalFundingMode.Dedicated,
                LinkedAccountId = ally.FinancialAccountId,
            });
            Txn(ally, new DateOnly(2026, 11, 2), -590);

            var model = (await service.GetGoalsAsync(Me)).Goals.Single();

            Assert.Equal((16590m, 16000m, 590m), (model.Saved, model.SavedAtMonthStart, model.ContributedThisMonth));
            await Assert.ThrowsAsync<CustomValidationException>(() =>
                service.AddContributionAsync(Me, new AddContributionModel(goal.SavingsGoalId, 50, null, null, null)));
        }

        [Fact]
        public async Task Shared_goals_show_what_the_account_has_left_over()
        {
            await PropertyTaxes();
            var account = Assert.Single((await service.GetGoalsAsync(Me)).Accounts);
            Assert.Equal((16590m, 3550m, 13040m), (account.Balance, account.Allocated, account.Unallocated));
        }

        [Fact]
        public async Task Archived_goals_drop_out()
        {
            var goal = await PropertyTaxes();
            await service.ArchiveGoalAsync(Me, goal.SavingsGoalId);
            Assert.Empty((await service.GetGoalsAsync(Me)).Goals);
        }
    }
}
