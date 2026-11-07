using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Categories;
using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using static Elysian.Tests.Financial.PlaidTestKit;

namespace Elysian.Tests.Financial
{
    public class CategoryCatalogTests
    {
        [Theory]
        [InlineData("FOOD_AND_DRINK_GROCERIES", Treatment.Spending, "groceries")]
        [InlineData("FOOD_AND_DRINK_FAST_FOOD", Treatment.Spending, "dining")]
        [InlineData("ENTERTAINMENT_TV_AND_MOVIES", Treatment.Spending, "subscriptions")]
        [InlineData("LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", Treatment.CardPayment, null)]
        [InlineData("TRANSFER_OUT_SAVINGS", Treatment.Transfer, null)]
        [InlineData("INCOME_WAGES", Treatment.Income, null)]
        [InlineData("INCOME_SALARY", Treatment.Income, null)]
        [InlineData("RENT_AND_UTILITIES_INTERNET_AND_CABLE", Treatment.Spending, "phone_internet")]
        [InlineData("MEDICAL_VETERINARY_SERVICES", Treatment.Spending, "pets")]
        public void Plaid_categories_map_to_a_treatment_and_category(string code, Treatment treatment, string? key)
        {
            var mapping = CategoryCatalog.ForPlaid(code)!;
            Assert.Equal(treatment, mapping.Treatment);
            Assert.Equal(key, mapping.CategoryKey);
        }

        [Fact]
        public void Unknown_and_other_codes_are_left_for_review() =>
            Assert.Null(CategoryCatalog.ForPlaid("OTHER_OTHER"));

        [Theory]
        [InlineData("TRADER JOE S #552", "trader joe s")]
        [InlineData("Trader Joe's #103", "trader joe s")]
        [InlineData("AMZN Mktp US*2K4LM0", "amzn mktp us k lm")]
        public void Merchant_keys_ignore_store_numbers(string text, string key) =>
            Assert.Equal(key, CategoryCatalog.MerchantKey(text));

        [Theory]
        [InlineData("Grocery", "groceries")]
        [InlineData("Rent", "housing")]
        [InlineData("Student Loan", "loans")]
        [InlineData("Hulu", null)]
        public void Existing_category_names_find_their_default(string name, string? key) =>
            Assert.Equal(key, CategoryCatalog.ForName(name)?.Key);
    }

    public sealed class TransactionClassifierTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));
        private readonly CategoryService categories;
        private readonly TransactionClassifier classifier;
        private readonly FinancialAccount card, checking, savings;
        private long nextId = 1;

        public TransactionClassifierTests()
        {
            var item = SeedBank(host, Protector(Settings())).Item;
            card = Account(item, "card", "credit", "credit card");
            checking = Account(item, "checking", "depository", "checking");
            savings = Account(item, "savings", "depository", "savings");
            host.Context.SaveChanges();
            categories = new CategoryService(host.Context, PlaidServiceTests.Mapper);
            classifier = new TransactionClassifier(host.Context, categories, clock);
        }

        private FinancialAccount Account(InstitutionAccessItem item, string id, string type, string subtype)
        {
            var a = new FinancialAccount { PlaidAccountId = id, Name = id, Type = type, Subtype = subtype, InstitutionAccessItemId = item.InstitutionAccessItemId, BudgetRole = FinancialAccount.DefaultRole(type, subtype) };
            host.Context.FinancialAccounts.Add(a);
            return a;
        }

        private BankTransaction Add(FinancialAccount account, decimal amount, string name, string? pfc, int day = 3)
        {
            var t = new BankTransaction
            {
                PlaidTransactionId = $"t{nextId++}",
                FinancialAccount = account,
                Amount = amount,
                Name = name,
                MerchantName = name,
                MerchantKey = CategoryCatalog.MerchantKey(name),
                PfcDetailed = pfc,
                Date = new DateOnly(2026, 11, day),
                BudgetDate = new DateOnly(2026, 11, day)
            };
            host.Context.BankTransactions.Add(t);
            return t;
        }

        private async Task<int> CategoryId(string key) =>
            (await host.Context.FinancialCategories.SingleAsync(c => c.UserId == Me && c.SystemKey == key)).FinancialCategoryId;

        [Fact]
        public async Task Plaid_categories_land_in_the_persons_categories()
        {
            var t = Add(card, 86.42m, "Publix", "FOOD_AND_DRINK_GROCERIES");
            await host.Context.SaveChangesAsync();

            await classifier.ClassifyAsync(Me);

            Assert.Equal(Treatment.Spending, t.Treatment);
            Assert.Equal(await CategoryId("groceries"), t.CategoryId);
            Assert.NotNull(t.ClassifiedAt);
        }

        [Fact]
        public async Task A_card_payment_and_the_checking_debit_that_paid_it_are_both_card_payments()
        {
            var debit = Add(checking, 612.40m, "BANK OF AMERICA ONLINE PMT", "LOAN_PAYMENTS_OTHER_PAYMENT", day: 1);
            var credit = Add(card, -612.40m, "PAYMENT THANK YOU", "TRANSFER_IN_ACCOUNT_TRANSFER", day: 2);
            await host.Context.SaveChangesAsync();

            await classifier.ClassifyAsync(Me);

            Assert.Equal(Treatment.CardPayment, debit.Treatment);
            Assert.Equal(Treatment.CardPayment, credit.Treatment);
            Assert.Null(debit.CategoryId);
        }

        [Fact]
        public async Task Money_moved_to_savings_is_a_transfer()
        {
            var outLeg = Add(checking, 490m, "TRANSFER TO SAVINGS", "TRANSFER_OUT_SAVINGS");
            var inLeg = Add(savings, -490m, "TRANSFER FROM CHECKING", "TRANSFER_IN_SAVINGS");
            await host.Context.SaveChangesAsync();

            await classifier.ClassifyAsync(Me);

            Assert.Equal(Treatment.Transfer, outLeg.Treatment);
            Assert.Equal(Treatment.Transfer, inLeg.Treatment);
        }

        [Fact]
        public async Task A_refund_nets_against_what_was_bought_there()
        {
            var purchase = Add(card, 129.99m, "Amazon", "GENERAL_MERCHANDISE_ONLINE_MARKETPLACES", day: 1);
            await host.Context.SaveChangesAsync();
            await classifier.ClassifyAsync(Me);

            var refund = Add(card, -32.50m, "Amazon", "GENERAL_MERCHANDISE_ONLINE_MARKETPLACES", day: 3);
            await host.Context.SaveChangesAsync();
            await classifier.ClassifyAsync(Me);

            Assert.Equal(Treatment.Refund, refund.Treatment);
            Assert.Equal(purchase.CategoryId, refund.CategoryId);
        }

        [Fact]
        public async Task A_paycheck_is_income_and_unknown_money_out_is_spending_to_review()
        {
            var paycheck = Add(checking, -3412.18m, "ACME CORP PAYROLL", "INCOME_SALARY");
            var mystery = Add(card, 60m, "VENMO *ALEX R", "OTHER_OTHER");
            await host.Context.SaveChangesAsync();

            await classifier.ClassifyAsync(Me);

            Assert.Equal(Treatment.Income, paycheck.Treatment);
            Assert.Equal(Treatment.Spending, mystery.Treatment);
            Assert.Null(mystery.CategoryId);
        }

        [Fact]
        public async Task A_rule_beats_Plaids_category_and_a_persons_choice_beats_the_rule()
        {
            await categories.EnsureDefaultsAsync(Me);
            var shopping = await CategoryId("shopping");
            var home = await CategoryId("home");
            host.Context.CategoryRules.Add(new CategoryRule { UserId = Me, Field = RuleField.Merchant, Match = RuleMatch.Equals, Value = "Target", SetCategoryId = home, Priority = 1 });
            var byRule = Add(card, 50m, "Target", "GENERAL_MERCHANDISE_SUPERSTORES");
            var byHand = Add(card, 20m, "Target", "GENERAL_MERCHANDISE_SUPERSTORES");
            byHand.CategoryId = shopping;
            byHand.CategorySource = ClassificationSource.User;
            await host.Context.SaveChangesAsync();

            await classifier.ClassifyAsync(Me, reclassifyAll: true);

            Assert.Equal(home, byRule.CategoryId);
            Assert.Equal(ClassificationSource.Rule, byRule.CategorySource);
            Assert.Equal(shopping, byHand.CategoryId);
        }

        [Fact]
        public async Task Categories_and_exclusions_from_the_older_budget_pages_are_kept()
        {
            await categories.EnsureDefaultsAsync(Me);
            var dining = await CategoryId("dining");
            var tagged = Add(card, 18.47m, "Chipotle", "FOOD_AND_DRINK_FAST_FOOD");
            var excluded = Add(card, 25m, "Something", "GENERAL_MERCHANDISE_OTHER_GENERAL_MERCHANDISE");
            var budget = new Budget { Name = "November", StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30), UserId = Me };
            host.Context.Budgets.Add(budget);
            await host.Context.SaveChangesAsync();
            host.Context.TransactionCategories.Add(new TransactionCategory { TransactionId = tagged.PlaidTransactionId, BudgetId = budget.BudgetId, FinancialCategoryId = dining });
            host.Context.BudgetExcludedTransactions.Add(new BudgetExcludedTransaction { TransactionId = excluded.PlaidTransactionId, BudgetId = budget.BudgetId });
            await host.Context.SaveChangesAsync();

            await classifier.ClassifyAsync(Me);

            Assert.Equal(ClassificationSource.User, tagged.CategorySource);
            Assert.Equal(Treatment.Hidden, excluded.Treatment);
            Assert.Equal(ClassificationSource.User, excluded.TreatmentSource);
        }

        [Fact]
        public async Task Existing_category_names_take_the_matching_defaults_and_the_rest_are_added_once()
        {
            host.Context.FinancialCategories.AddRange(
                new FinancialCategory { UserId = Me, Name = "Grocery" },
                new FinancialCategory { UserId = Me, Name = "Hulu" });
            await host.Context.SaveChangesAsync();

            await categories.EnsureDefaultsAsync(Me);
            await categories.EnsureDefaultsAsync(Me);

            var mine = await host.Context.FinancialCategories.Where(c => c.UserId == Me).ToListAsync();
            Assert.Equal("groceries", mine.Single(c => c.Name == "Grocery").SystemKey);
            Assert.Null(mine.Single(c => c.Name == "Hulu").SystemKey);
            Assert.DoesNotContain(mine, c => c.Name == "Groceries");
            Assert.Equal(CategoryCatalog.Defaults.Count + 1, mine.Count);
        }

        [Fact]
        public async Task Merging_a_category_moves_its_transactions_and_hides_it()
        {
            await categories.EnsureDefaultsAsync(Me);
            var hulu = new FinancialCategory { UserId = Me, Name = "Hulu" };
            host.Context.FinancialCategories.Add(hulu);
            var t = Add(card, 17.99m, "Hulu", "ENTERTAINMENT_TV_AND_MOVIES");
            await host.Context.SaveChangesAsync();
            t.CategoryId = hulu.FinancialCategoryId;
            t.CategorySource = ClassificationSource.User;
            await host.Context.SaveChangesAsync();

            var subscriptions = await CategoryId("subscriptions");
            await categories.ArchiveCategoryAsync(Me, hulu.FinancialCategoryId, subscriptions);

            Assert.Equal(subscriptions, t.CategoryId);
            Assert.Equal(ClassificationSource.User, t.CategorySource);
            Assert.True(hulu.IsArchived);
            Assert.DoesNotContain(await categories.GetCategoriesAsync(Me), c => c.Name == "Hulu");
        }

        [Fact]
        public async Task Someone_elses_category_cant_be_merged_into()
        {
            await categories.EnsureDefaultsAsync(Me);
            host.Context.FinancialCategories.Add(new FinancialCategory { UserId = "someone-else", Name = "Theirs" });
            await host.Context.SaveChangesAsync();
            var theirs = await host.Context.FinancialCategories.SingleAsync(c => c.Name == "Theirs");

            var other = await CategoryId("other");
            await Assert.ThrowsAsync<NotFoundException>(() => categories.ArchiveCategoryAsync(Me, other, theirs.FinancialCategoryId));
        }

        public void Dispose() => host.Dispose();
    }

    public sealed class TransactionServiceTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));
        private readonly CategoryService categories;
        private readonly TransactionService service;
        private readonly FinancialAccount card;

        public TransactionServiceTests()
        {
            var item = SeedBank(host, Protector(Settings())).Item;
            card = new FinancialAccount { PlaidAccountId = "card", Name = "Gold Card", Type = "credit", Subtype = "credit card", InstitutionAccessItemId = item.InstitutionAccessItemId };
            host.Context.FinancialAccounts.Add(card);
            host.Context.SaveChanges();
            categories = new CategoryService(host.Context, PlaidServiceTests.Mapper);
            service = new TransactionService(host.Context, new TransactionClassifier(host.Context, categories, clock));
        }

        private BankTransaction Add(decimal amount, string name, string? pfc, int day = 3)
        {
            var t = new BankTransaction
            {
                PlaidTransactionId = Guid.NewGuid().ToString("N"),
                FinancialAccount = card,
                Amount = amount,
                Name = name,
                MerchantName = name,
                MerchantKey = CategoryCatalog.MerchantKey(name),
                PfcDetailed = pfc,
                Date = new DateOnly(2026, 11, day),
                BudgetDate = new DateOnly(2026, 11, day)
            };
            host.Context.BankTransactions.Add(t);
            host.Context.SaveChanges();
            return t;
        }

        private async Task<int> CategoryId(string key) =>
            (await host.Context.FinancialCategories.SingleAsync(c => c.UserId == Me && c.SystemKey == key)).FinancialCategoryId;

        [Fact]
        public async Task Search_counts_each_view_and_totals_counted_spending()
        {
            Add(86.42m, "Publix", "FOOD_AND_DRINK_GROCERIES");
            Add(-2450m, "PAYMENT THANK YOU", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT");
            Add(60m, "VENMO *ALEX R", "OTHER_OTHER");
            Add(-32.50m, "Publix", "FOOD_AND_DRINK_GROCERIES");

            var result = await service.SearchAsync(Me, new TransactionSearch(View: TransactionView.All));

            Assert.Equal(new TransactionViewCounts(All: 4, Spending: 3, NotCounted: 1, Review: 1, Pending: 0), result.Counts);
            Assert.Equal(86.42m + 60m - 32.50m, result.CountedTotal);

            var review = await service.SearchAsync(Me, new TransactionSearch(View: TransactionView.Review));
            Assert.Equal("VENMO *ALEX R", review.Items.Single().name);

            var byText = await service.SearchAsync(Me, new TransactionSearch(Query: "publix"));
            Assert.Equal(2, byText.Total);
        }

        [Fact]
        public async Task A_split_must_add_up_and_then_counts_in_each_category()
        {
            var t = Add(129.99m, "Amazon", "GENERAL_MERCHANDISE_ONLINE_MARKETPLACES");
            await categories.EnsureDefaultsAsync(Me);
            var shopping = await CategoryId("shopping");
            var home = await CategoryId("home");

            await Assert.ThrowsAsync<CustomValidationException>(() => service.UpdateAsync(Me,
                new TransactionUpdate(t.BankTransactionId, Splits: [new(shopping, 90m, null), new(home, 30m, null)])));

            var updated = await service.UpdateAsync(Me, new TransactionUpdate(t.BankTransactionId,
                Splits: [new(shopping, 91.87m, null), new(home, 38.12m, "liners")], Notes: "Phone case + liners"));

            Assert.Equal(2, updated.Splits.Count);
            Assert.Equal("Phone case + liners", updated.Notes);

            var budget = new BudgetViewModel("November", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30), [], [updated], [], []);
            Assert.Equal(91.87m, budget.BudgetCategoryData.Single(c => c.FinancialCategoryId == shopping).Sum);
            Assert.Equal(38.12m, budget.BudgetCategoryData.Single(c => c.FinancialCategoryId == home).Sum);
        }

        [Fact]
        public async Task Always_do_this_creates_a_rule_that_updates_past_transactions()
        {
            var first = Add(60m, "VENMO *ALEX R", "OTHER_OTHER", day: 1);
            var second = Add(60m, "VENMO *ALEX R", "OTHER_OTHER", day: 5);
            await categories.EnsureDefaultsAsync(Me);
            var other = await CategoryId("other");

            await service.UpdateAsync(Me, new TransactionUpdate(first.BankTransactionId, CategoryId: other,
                CreateRule: new RuleFromTransaction(RuleField.Merchant, "VENMO *ALEX R", ApplyToPast: true)));

            var rule = (await service.GetRulesAsync(Me)).Single();
            Assert.Equal(other, rule.SetCategoryId);
            Assert.Equal(RuleDirection.Out, rule.Direction);
            Assert.Equal(other, second.CategoryId);
            Assert.Equal(ClassificationSource.Rule, second.CategorySource);
            Assert.Equal(ClassificationSource.User, first.CategorySource);
        }

        [Fact]
        public async Task Deleting_a_rule_hands_its_transactions_back_to_the_automatic_rules()
        {
            var t = Add(18.47m, "Chipotle", "FOOD_AND_DRINK_FAST_FOOD");
            await categories.EnsureDefaultsAsync(Me);
            var saved = await service.SaveRuleAsync(Me, new CategoryRuleModel
            {
                Field = RuleField.Merchant, Match = RuleMatch.Equals, Value = "Chipotle", SetCategoryId = await CategoryId("other")
            }, applyToPast: true);
            Assert.Equal(1, saved.Matches);
            Assert.Equal(await CategoryId("other"), t.CategoryId);

            await service.DeleteRuleAsync(Me, saved.Rule.CategoryRuleId);

            Assert.Equal(await CategoryId("dining"), t.CategoryId);
        }

        [Fact]
        public async Task Hiding_a_transaction_takes_it_out_of_the_budget()
        {
            var t = Add(41.25m, "Shell", "TRANSPORTATION_GAS");

            var updated = await service.UpdateAsync(Me, new TransactionUpdate(t.BankTransactionId, Treatment: Treatment.Hidden));

            Assert.False(updated.IsCounted);
            var budget = new BudgetViewModel("November", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30), [], [updated], [], []);
            Assert.Empty(budget.Transactions);
            Assert.Single(budget.ExcludedTransactions);
        }

        [Fact]
        public async Task An_account_that_doesnt_count_as_spending_is_left_out()
        {
            card.BudgetRole = AccountBudgetRole.Ignore;
            await host.Context.SaveChangesAsync();
            Add(41.25m, "Shell", "TRANSPORTATION_GAS");

            var result = await service.SearchAsync(Me, new TransactionSearch());

            Assert.Equal(0, result.Counts.Spending);
            Assert.Equal(0m, result.CountedTotal);
        }

        [Fact]
        public async Task Someone_elses_transaction_is_not_found()
        {
            var t = Add(10m, "Publix", "FOOD_AND_DRINK_GROCERIES");

            await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync("someone-else", new TransactionUpdate(t.BankTransactionId, Notes: "x")));
            Assert.Equal(0, (await service.SearchAsync("someone-else", new TransactionSearch())).Total);
        }

        public void Dispose() => host.Dispose();
    }
}
