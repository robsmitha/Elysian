using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Tests.Financial
{
    public sealed class BudgetServiceTests : IDisposable
    {
        private const string Me = "me-swa-id";
        private const string Someone = "someone-else-swa-id";

        private readonly TestHost host = new();
        private readonly BudgetService service;
        private int groceries, dining, myBank, theirBank;

        public BudgetServiceTests()
        {
            service = new BudgetService(host.Context, PlaidServiceTests.Mapper);
            Seed();
        }

        private void Seed()
        {
            var ctx = host.Context;
            var me = TestHost.User(1, Me, "me@example.com");
            var them = TestHost.User(2, Someone, "them@example.com");
            ctx.Users.AddRange(me, them);

            var g = new FinancialCategory { Name = "Groceries", UserId = Me };
            var d = new FinancialCategory { Name = "Dining", UserId = Me };
            ctx.FinancialCategories.AddRange(g, d);

            var mine = new InstitutionAccessItem { AccessToken = "access-1", ItemId = "item-1", InstitutionId = "ins_1" };
            var theirs = new InstitutionAccessItem { AccessToken = "access-2", ItemId = "item-2", InstitutionId = "ins_2" };
            ctx.InstitutionAccessItems.AddRange(mine, theirs);
            ctx.SaveChanges();

            ctx.InstitutionAccessItemUsers.AddRange(
                new InstitutionAccessItemUser { UserId = me.UserId, InstitutionAccessItemId = mine.InstitutionAccessItemId },
                new InstitutionAccessItemUser { UserId = them.UserId, InstitutionAccessItemId = theirs.InstitutionAccessItemId });
            ctx.SaveChanges();

            (groceries, dining, myBank, theirBank) = (g.FinancialCategoryId, d.FinancialCategoryId, mine.InstitutionAccessItemId, theirs.InstitutionAccessItemId);
        }

        private BudgetModel Budget(string name, params int[] categoryIds) => new()
        {
            Name = name,
            StartDate = new DateTime(2026, 11, 1),
            EndDate = new DateTime(2026, 11, 30),
            Categories = categoryIds.Select(id => new FinancialCategoryModel { FinancialCategoryId = id }).ToList(),
            BudgetAccessItems = [new InstitutionAccessItemModel { InstitutionAccessItemId = myBank }]
        };

        [Fact]
        public async Task Editing_one_budget_leaves_another_budgets_categories_alone()
        {
            var november = await service.AddBudgetAsync(Me, Budget("November", groceries, dining));
            var october = await service.AddBudgetAsync(Me, Budget("October", groceries, dining));

            // Remove Groceries from November only
            var edit = Budget("November", dining);
            edit.BudgetId = november.BudgetId;
            await service.UpdateBudgetAsync(Me, edit);

            var octoberCategories = await service.GetBudgetCategoriesAsync(Me, october.BudgetId);
            var novemberCategories = await service.GetBudgetCategoriesAsync(Me, november.BudgetId);
            Assert.Equal(new[] { dining, groceries }.Order(), octoberCategories.Select(c => c.FinancialCategoryId).Order());
            Assert.Equal([dining], novemberCategories.Select(c => c.FinancialCategoryId));
        }

        [Fact]
        public async Task Saving_without_categories_or_banks_does_not_throw()
        {
            var budget = await service.AddBudgetAsync(Me, new BudgetModel { Name = "Bare", StartDate = DateTime.Today, EndDate = DateTime.Today });

            var edit = new BudgetModel { BudgetId = budget.BudgetId, Name = "Bare", StartDate = DateTime.Today, EndDate = DateTime.Today };
            await service.UpdateBudgetAsync(Me, edit);

            Assert.Empty(await service.GetBudgetCategoriesAsync(Me, budget.BudgetId));
        }

        [Fact]
        public async Task Excluding_a_transaction_twice_is_harmless()
        {
            var budget = await service.AddBudgetAsync(Me, Budget("November"));

            await service.SetExcludedTransactionAsync(Me, budget.BudgetId, "txn-1");
            await service.SetExcludedTransactionAsync(Me, budget.BudgetId, "txn-1");

            Assert.Single(await service.GetExcludedTransactionsAsync(Me, budget.BudgetId));
        }

        [Fact]
        public async Task Someone_elses_budget_is_not_found()
        {
            var budget = await service.AddBudgetAsync(Me, Budget("November", groceries));

            Assert.Null(await service.GetBudgetAsync(Someone, budget.BudgetId));
            await Assert.ThrowsAsync<NotFoundException>(() => service.GetBudgetCategoriesAsync(Someone, budget.BudgetId));
            await Assert.ThrowsAsync<NotFoundException>(() => service.SetExcludedTransactionAsync(Someone, budget.BudgetId, "txn-1"));
            await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateBudgetCategoryEstimateAsync(Someone, budget.BudgetId, groceries, 10));

            var edit = Budget("Hijacked");
            edit.BudgetId = budget.BudgetId;
            await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateBudgetAsync(Someone, edit));
            Assert.Equal("November", (await host.Context.Budgets.SingleAsync()).Name);
        }

        [Fact]
        public async Task A_budget_cannot_include_someone_elses_bank()
        {
            var request = Budget("November");
            request.BudgetAccessItems = [new InstitutionAccessItemModel { InstitutionAccessItemId = theirBank }];

            await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.AddBudgetAsync(Me, request));
            Assert.Empty(host.Context.Budgets);
        }

        [Fact]
        public async Task Adding_a_category_that_is_already_there_only_updates_its_estimate()
        {
            var budget = await service.AddBudgetAsync(Me, Budget("November", groceries));

            await service.AddBudgetCategoryAsync(Me, budget.BudgetId, groceries, 800);
            await service.AddBudgetCategoryAsync(Me, budget.BudgetId, groceries);

            var category = Assert.Single(await service.GetBudgetCategoriesAsync(Me, budget.BudgetId));
            Assert.Equal(800, category.Estimate);
        }

        public void Dispose() => host.Dispose();
    }
}
