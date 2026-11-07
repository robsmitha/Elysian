using System.Text.Json;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Assistant;
using Elysian.Application.Features.Assistant.Models;
using Elysian.Application.Features.Financial.Categories;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Assistant;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Elysian.Tests.Financial.PlaidTestKit;

namespace Elysian.Tests.Assistant
{
    /// <summary>
    /// Proposals, confirm, cancel and undo (docs/assistant-actions-plan.md §7), on an in-memory database with two people.
    /// The rollback of a failed confirm needs a real database; here a failure is checked to leave nothing applied.
    /// </summary>
    public sealed class AssistantActionTests : IDisposable
    {
        private const string Other = "other-swa-id";
        private readonly TestHost host = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero));
        private static readonly DateOnly Today = new(2026, 11, 7);
        private readonly FinancialAccount checking, othersChecking;
        private readonly FinancialCategory groceries, coffee, restaurants, gas, othersCategory;
        private readonly BankTransaction byHand, automatic, othersStarbucks;
        private readonly AssistantTools tools;
        private readonly TransactionService transactions;
        private readonly CategoryService categories;
        private readonly BudgetMonthService budgets;
        private long nextId = 1;

        public AssistantActionTests()
        {
            var protector = Protector(Settings());
            var mine = SeedBank(host, protector).Item;
            var theirs = SeedBank(host, protector, "access-sandbox-2", Other, 2, "item-2").Item;
            checking = new FinancialAccount { PlaidAccountId = "chk", Name = "Checking", Mask = "4410", Type = "depository", InstitutionAccessItemId = mine.InstitutionAccessItemId, BudgetRole = AccountBudgetRole.Spending };
            othersChecking = new FinancialAccount { PlaidAccountId = "chk2", Name = "Their checking", Mask = "9999", Type = "depository", InstitutionAccessItemId = theirs.InstitutionAccessItemId, BudgetRole = AccountBudgetRole.Spending };
            host.Context.FinancialAccounts.AddRange(checking, othersChecking);
            groceries = Category("Groceries", "groceries");
            coffee = Category("Coffee", "coffee");
            restaurants = Category("Restaurants", "dining");
            gas = Category("Gas", "gas");
            othersCategory = new FinancialCategory { Name = "Coffee", UserId = Other, Group = CategoryGroup.Flexible, SystemKey = "coffee" };
            host.Context.FinancialCategories.Add(othersCategory);
            host.Context.SaveChanges();

            byHand = Spend(checking, new DateOnly(2026, 10, 2), 6.50m, restaurants, "Starbucks");
            automatic = Spend(checking, new DateOnly(2026, 11, 2), 7.25m, restaurants, "STARBUCKS STORE 1234", ClassificationSource.Rule);
            Spend(checking, new DateOnly(2026, 11, 3), 214, groceries, "Trader Joe's");
            Spend(checking, new DateOnly(2026, 11, 4), 41, null, "Shell", ClassificationSource.Auto);
            othersStarbucks = Spend(othersChecking, new DateOnly(2026, 11, 1), 999, othersCategory, "Starbucks");
            host.Context.SaveChanges();

            categories = new CategoryService(host.Context, Financial.PlaidServiceTests.Mapper);
            var classifier = new TransactionClassifier(host.Context, categories, clock);
            transactions = new TransactionService(host.Context, classifier);
            budgets = new BudgetMonthService(host.Context, classifier, clock);
            tools = new AssistantTools(host.Context, budgets, new GoalService(host.Context, clock), new DebtService(host.Context, clock), transactions, clock);
        }

        public void Dispose() => host.Dispose();

        private FinancialCategory Category(string name, string key)
        {
            var c = new FinancialCategory { Name = name, UserId = Me, Group = CategoryGroup.Flexible, SystemKey = key };
            host.Context.FinancialCategories.Add(c);
            return c;
        }

        private BankTransaction Spend(FinancialAccount account, DateOnly date, decimal amount, FinancialCategory? category, string name,
            ClassificationSource source = ClassificationSource.User)
        {
            var t = new BankTransaction
            {
                PlaidTransactionId = $"t{nextId++}",
                FinancialAccountId = account.FinancialAccountId,
                Amount = amount,
                Name = name,
                MerchantName = name,
                MerchantKey = CategoryCatalog.MerchantKey(name),
                Date = date,
                BudgetDate = date,
                CategoryId = category?.FinancialCategoryId,
                CategorySource = source,
                Treatment = Treatment.Spending,
                TreatmentSource = source,
                ClassifiedAt = clock.GetUtcNow(),
            };
            host.Context.BankTransactions.Add(t);
            return t;
        }

        private AssistantActions Actions(ITransactionService? transactionService = null) =>
            new(host.Context, tools, transactionService ?? transactions, budgets, categories, clock, NullLogger<AssistantActions>.Instance);

        private async Task<(ToolOutcome Outcome, Guid Id)> Propose(string tool, string input, string user = Me)
        {
            var outcome = await tools.RunAsync(user, Today, new ModelToolCall("toolu_1", tool, JsonDocument.Parse(input).RootElement), CancellationToken.None);
            Assert.False(outcome.IsError, outcome.Json);
            Assert.NotNull(outcome.Action);
            return (outcome, outcome.Action!.Id);
        }

        private Task<(ToolOutcome Outcome, Guid Id)> MoveStarbucksToCoffee(bool rule = false) =>
            Propose("propose_recategorize", $$"""{ "merchant": "starbucks", "to_category_id": {{coffee.FinancialCategoryId}}, "include_rule": {{(rule ? "true" : "false")}} }""");

        private async Task<BankTransaction> Reload(BankTransaction t)
        {
            host.Context.ChangeTracker.Clear();
            return await host.Context.BankTransactions.SingleAsync(x => x.BankTransactionId == t.BankTransactionId);
        }

        // ---- proposing ----

        [Fact]
        public async Task A_proposal_finds_exactly_my_matching_transactions_and_changes_nothing()
        {
            var (outcome, id) = await MoveStarbucksToCoffee();
            using var doc = JsonDocument.Parse(outcome.Json);
            var root = doc.RootElement;

            Assert.Equal(id.ToString(), root.GetProperty("action_id").GetString());
            Assert.Equal(2, root.GetProperty("transactions").GetInt32());
            Assert.Equal(13.75m, root.GetProperty("total").GetDecimal());
            Assert.Equal(1, root.GetProperty("set_by_hand").GetInt32());
            Assert.Contains("Nothing has changed", root.GetProperty("note").GetString());

            var card = outcome.Action!;
            Assert.Equal("Move 2 Starbucks transactions to Coffee", card.Title);
            Assert.Equal(["STARBUCKS STORE 1234", "Starbucks"], card.Rows.Select(r => r.Merchant));
            Assert.All(card.Rows, r => Assert.Equal(("Restaurants", "Coffee"), (r.From, r.To)));
            Assert.Contains(card.Warnings, w => w.StartsWith("1 of these you set by hand"));
            Assert.Equal("Also do this for future Starbucks charges", card.RuleOffer!.Label);
            Assert.False(card.RuleOffer.Ticked);

            var saved = await host.Context.AssistantActions.SingleAsync();
            Assert.Equal((AssistantActionStatus.Proposed, Me, 2), (saved.Status, saved.UserId, saved.AffectedCount));
            Assert.Equal(clock.GetUtcNow().AddMinutes(15), saved.ExpiresAt);
            Assert.DoesNotContain(othersStarbucks.BankTransactionId.ToString(), saved.ChangeJson.Split(',', '[', ']'));
            Assert.Equal(restaurants.FinancialCategoryId, (await Reload(byHand)).CategoryId);
        }

        [Fact]
        public async Task Someone_elses_category_and_an_unnarrowed_change_are_refused()
        {
            var theirs = await tools.RunAsync(Me, Today, new ModelToolCall("t", "propose_recategorize",
                JsonDocument.Parse($$"""{ "merchant": "starbucks", "to_category_id": {{othersCategory.FinancialCategoryId}} }""").RootElement), default);
            var everything = await tools.RunAsync(Me, Today, new ModelToolCall("t", "propose_recategorize",
                JsonDocument.Parse($$"""{ "to_category_id": {{coffee.FinancialCategoryId}} }""").RootElement), default);

            Assert.True(theirs.IsError);
            Assert.Contains("Unknown category", theirs.Json);
            Assert.True(everything.IsError);
            Assert.Contains("Say which transactions", everything.Json);
            Assert.Empty(host.Context.AssistantActions);
        }

        [Fact]
        public async Task More_than_500_at_once_is_refused()
        {
            for (var i = 0; i < AssistantTools.MaxActionRows + 1; i++) Spend(checking, Today.AddDays(-(i % 300)), 3, restaurants, "Dunkin");
            await host.Context.SaveChangesAsync();

            var r = await tools.RunAsync(Me, Today, new ModelToolCall("t", "propose_recategorize",
                JsonDocument.Parse($$"""{ "merchant": "dunkin", "to_category_id": {{coffee.FinancialCategoryId}} }""").RootElement), default);

            Assert.True(r.IsError);
            Assert.Contains("at most 500", r.Json);
        }

        // ---- confirming ----

        [Fact]
        public async Task Confirm_applies_exactly_the_proposal_and_a_second_confirm_returns_the_same()
        {
            var (_, id) = await MoveStarbucksToCoffee();

            var first = await Actions().ConfirmAsync(Me, id, null);
            var second = await Actions().ConfirmAsync(Me, id, null);

            Assert.Equal(("done", "2 moved to Coffee"), (first.Outcome, first.Message));
            Assert.Equal(("confirmed", true), (first.Action.Status, first.Action.CanUndo));
            Assert.Equal(first.Message, second.Message);
            var a = await Reload(byHand);
            var b = await Reload(automatic);
            Assert.Equal((coffee.FinancialCategoryId, ClassificationSource.User), (a.CategoryId, a.CategorySource));
            Assert.Equal((coffee.FinancialCategoryId, ClassificationSource.User), (b.CategoryId, b.CategorySource));
            Assert.Equal(othersCategory.FinancialCategoryId, (await Reload(othersStarbucks)).CategoryId);
            Assert.Empty(host.Context.CategoryRules);
        }

        [Fact]
        public async Task Someone_else_cant_confirm_it()
        {
            var (_, id) = await MoveStarbucksToCoffee();
            await Assert.ThrowsAsync<NotFoundException>(() => Actions().ConfirmAsync(Other, id, null));
            Assert.Equal(AssistantActionStatus.Proposed, (await host.Context.AssistantActions.SingleAsync()).Status);
        }

        [Fact]
        public async Task After_15_minutes_it_has_expired()
        {
            var (_, id) = await MoveStarbucksToCoffee();
            clock.Now = clock.Now.AddMinutes(16);

            var ex = await Assert.ThrowsAsync<ConflictException>(() => Actions().ConfirmAsync(Me, id, null));
            Assert.Equal(AssistantActions.Expired, ex.Message);
            Assert.Equal(restaurants.FinancialCategoryId, (await Reload(byHand)).CategoryId);
        }

        [Fact]
        public async Task A_new_matching_transaction_makes_it_stale_and_the_fresh_one_can_be_confirmed()
        {
            var (_, id) = await MoveStarbucksToCoffee();
            Spend(checking, new DateOnly(2026, 11, 7), 5.40m, restaurants, "Starbucks");
            await host.Context.SaveChangesAsync();

            var stale = await Actions().ConfirmAsync(Me, id, null);
            Assert.Equal("stale", stale.Outcome);
            Assert.Equal("Move 3 Starbucks transactions to Coffee", stale.Action.Title);
            Assert.Equal(restaurants.FinancialCategoryId, (await Reload(byHand)).CategoryId);

            var done = await Actions().ConfirmAsync(Me, id, null);
            Assert.Equal(("done", "3 moved to Coffee"), (done.Outcome, done.Message));
        }

        [Fact]
        public async Task A_change_made_by_hand_since_also_makes_it_stale()
        {
            var (_, id) = await MoveStarbucksToCoffee();
            await transactions.UpdateAsync(Me, new TransactionUpdate(automatic.BankTransactionId, groceries.FinancialCategoryId, false, null, false, false, null, null, null));

            Assert.Equal("stale", (await Actions().ConfirmAsync(Me, id, null)).Outcome);
        }

        [Fact]
        public async Task Cancelled_it_cant_be_confirmed()
        {
            var (_, id) = await MoveStarbucksToCoffee();
            Assert.Equal("cancelled", (await Actions().CancelAsync(Me, id)).Outcome);
            await Assert.ThrowsAsync<ConflictException>(() => Actions().ConfirmAsync(Me, id, null));
        }

        [Fact]
        public async Task Twenty_confirmed_changes_a_day_is_the_limit()
        {
            for (var i = 0; i < AssistantActions.MaxConfirmsPerDay; i++)
            {
                host.Context.AssistantActions.Add(new AssistantAction
                {
                    AssistantActionId = Guid.NewGuid(), UserId = Me, Status = AssistantActionStatus.Confirmed,
                    CreatedAt = clock.Now.AddHours(-2), ExpiresAt = clock.Now, ConfirmedAt = clock.Now.AddHours(-1),
                });
            }
            await host.Context.SaveChangesAsync();
            var (_, id) = await MoveStarbucksToCoffee();

            await Assert.ThrowsAsync<TooManyRequestsException>(() => Actions().ConfirmAsync(Me, id, null));
        }

        [Fact]
        public async Task A_failure_while_applying_marks_it_failed_and_changes_nothing()
        {
            var (_, id) = await MoveStarbucksToCoffee();

            var ex = await Assert.ThrowsAsync<ConflictException>(() => Actions(new FailingTransactions(transactions)).ConfirmAsync(Me, id, null));

            Assert.Equal(AssistantActions.Couldnt, ex.Message);
            Assert.Equal(AssistantActionStatus.Failed, (await host.Context.AssistantActions.SingleAsync()).Status);
            Assert.Equal(restaurants.FinancialCategoryId, (await Reload(byHand)).CategoryId);
        }

        [Fact]
        public async Task The_tick_box_adds_a_rule_and_undo_removes_it()
        {
            var (_, id) = await MoveStarbucksToCoffee();

            var done = await Actions().ConfirmAsync(Me, id, includeRule: true);
            var rule = await host.Context.CategoryRules.SingleAsync();
            Assert.Equal((RuleField.Merchant, "starbucks", coffee.FinancialCategoryId), (rule.Field, rule.Value, rule.SetCategoryId));
            Assert.Contains("a rule was added", done.Message);

            var undone = await Actions().UndoAsync(Me, id);
            Assert.Contains("The rule was removed", undone.Message);
            Assert.Empty(host.Context.CategoryRules);
        }

        // ---- undoing ----

        [Fact]
        public async Task Undo_puts_rows_back_and_leaves_ones_changed_since()
        {
            var (_, id) = await MoveStarbucksToCoffee();
            await Actions().ConfirmAsync(Me, id, null);
            await transactions.UpdateAsync(Me, new TransactionUpdate(automatic.BankTransactionId, groceries.FinancialCategoryId, false, null, false, false, null, null, null));

            var undone = await Actions().UndoAsync(Me, id);

            Assert.Equal("Undone for 1; 1 you changed since was left as it is.", undone.Message);
            Assert.Equal("undone", undone.Action.Status);
            var a = await Reload(byHand);
            Assert.Equal((restaurants.FinancialCategoryId, ClassificationSource.User), (a.CategoryId, a.CategorySource));
            Assert.Equal(groceries.FinancialCategoryId, (await Reload(automatic)).CategoryId);
        }

        [Fact]
        public async Task Undo_restores_who_set_it_too()
        {
            var (_, id) = await MoveStarbucksToCoffee();
            await Actions().ConfirmAsync(Me, id, null);

            await Actions().UndoAsync(Me, id);

            var b = await Reload(automatic);
            Assert.Equal((restaurants.FinancialCategoryId, ClassificationSource.Rule), (b.CategoryId, b.CategorySource));
        }

        [Fact]
        public async Task After_24_hours_it_cant_be_undone()
        {
            var (_, id) = await MoveStarbucksToCoffee();
            await Actions().ConfirmAsync(Me, id, null);
            clock.Now = clock.Now.AddHours(25);

            await Assert.ThrowsAsync<ConflictException>(() => Actions().UndoAsync(Me, id));
        }

        // ---- the other kinds ----

        [Fact]
        public async Task Hiding_transactions_changes_how_they_count_and_undo_counts_them_again()
        {
            var (outcome, id) = await Propose("propose_set_treatment", """{ "merchant": "starbucks", "treatment": "Hidden" }""");
            Assert.Equal("Hide 2 Starbucks transactions", outcome.Action!.Title);

            Assert.Equal("2 hidden", (await Actions().ConfirmAsync(Me, id, null)).Message);
            Assert.Equal(Treatment.Hidden, (await Reload(byHand)).Treatment);

            await Actions().UndoAsync(Me, id);
            Assert.Equal(Treatment.Spending, (await Reload(byHand)).Treatment);
        }

        [Fact]
        public async Task A_rule_proposal_counts_the_past_transactions_it_would_change()
        {
            var (outcome, id) = await Propose("propose_rule", $$"""{ "match_text": "shell", "set_category_id": {{gas.FinancialCategoryId}} }""");
            using var doc = JsonDocument.Parse(outcome.Json);

            Assert.Equal("New rule: Merchant contains “shell” → Gas", outcome.Action!.Title);
            Assert.Equal(1, doc.RootElement.GetProperty("past_transactions_it_changes").GetInt32());

            await Actions().ConfirmAsync(Me, id, null);
            Assert.Equal(gas.FinancialCategoryId, (await host.Context.CategoryRules.SingleAsync()).SetCategoryId);

            await Actions().UndoAsync(Me, id);
            Assert.Empty(host.Context.CategoryRules);
        }

        [Fact]
        public async Task A_target_shows_before_and_after_and_undo_puts_it_back()
        {
            var (outcome, id) = await Propose("propose_target", $$"""{ "category_id": {{groceries.FinancialCategoryId}}, "amount": 400 }""");
            var card = outcome.Action!;

            Assert.Equal("Set Groceries to $400 for November 2026", card.Title);
            Assert.Equal(new AssistantActionChange("Target", "$0", "$400"), card.Changes[0]);
            Assert.Equal("Not yet assigned", card.Changes[1].Label);

            Assert.Equal("Groceries set to $400.", (await Actions().ConfirmAsync(Me, id, null)).Message);
            var month = await budgets.GetMonthAsync(Me, 2026, 11, Today);
            Assert.Equal(400m, month.Lines.Single(l => l.CategoryId == groceries.FinancialCategoryId).Target);

            await Actions().UndoAsync(Me, id);
            Assert.DoesNotContain(host.Context.BudgetTargets, t => t.CategoryId == groceries.FinancialCategoryId);
        }

        [Fact]
        public async Task A_new_category_can_be_added_and_undone()
        {
            var (outcome, id) = await Propose("propose_category", """{ "op": "add", "name": "Pets" }""");
            Assert.Equal("Make a new category: Pets", outcome.Action!.Title);

            await Actions().ConfirmAsync(Me, id, null);
            Assert.Contains(host.Context.FinancialCategories, c => c.UserId == Me && c.Name == "Pets" && !c.IsArchived);

            await Actions().UndoAsync(Me, id);
            Assert.True((await host.Context.FinancialCategories.SingleAsync(c => c.Name == "Pets")).IsArchived);
        }

        [Fact]
        public async Task A_name_thats_taken_isnt_proposed()
        {
            var r = await tools.RunAsync(Me, Today, new ModelToolCall("t", "propose_category", JsonDocument.Parse("""{ "op": "add", "name": "coffee" }""").RootElement), default);
            Assert.True(r.IsError);
            Assert.Contains("already a category called Coffee", r.Json);
        }

        [Fact]
        public async Task A_rename_can_be_undone()
        {
            var (_, id) = await Propose("propose_category", $$"""{ "op": "rename", "category_id": {{restaurants.FinancialCategoryId}}, "name": "Eating out" }""");
            await Actions().ConfirmAsync(Me, id, null);
            Assert.Equal("Eating out", (await categories.GetCategoryAsync(Me, restaurants.FinancialCategoryId))!.Name);

            await Actions().UndoAsync(Me, id);
            Assert.Equal("Restaurants", (await categories.GetCategoryAsync(Me, restaurants.FinancialCategoryId))!.Name);
        }

        [Fact]
        public async Task A_merge_says_it_cant_be_undone_and_moves_everything()
        {
            var (outcome, id) = await Propose("propose_category", $$"""{ "op": "merge", "category_id": {{restaurants.FinancialCategoryId}}, "into_category_id": {{coffee.FinancialCategoryId}} }""");
            var card = outcome.Action!;

            Assert.True(card.Destructive);
            Assert.Equal("Merge and archive Restaurants", card.ConfirmLabel);
            Assert.Equal("2", card.Facts.Single(f => f.Label == "Transactions that move").Value);

            var done = await Actions().ConfirmAsync(Me, id, null);
            Assert.False(done.Action.CanUndo);
            Assert.Equal(coffee.FinancialCategoryId, (await Reload(byHand)).CategoryId);
            await Assert.ThrowsAsync<ConflictException>(() => Actions().UndoAsync(Me, id));
        }

        // ---- the whole flow, offline ----

        private AssistantService Service() =>
            new(host.Context, new Monitor<AssistantSettings>(new AssistantSettings { Enabled = true, PerDay = 30, PerMinute = 100 }),
                new AssistantLimiter(host.Context, new Monitor<AssistantSettings>(new AssistantSettings { Enabled = true, PerDay = 30, PerMinute = 100 }), clock),
                tools, new FakeAssistantModel(), new TransactionClassifier(host.Context, categories, clock), clock, NullLogger<AssistantService>.Instance);

        [Fact]
        public async Task Move_all_my_starbucks_to_coffee_gets_a_card_to_confirm()
        {
            var reply = await Service().AskAsync(Me, new AskAssistantRequest("Move all my Starbucks to Coffee", null, Today, "transactions", null));

            Assert.Equal("answered", reply.Status);
            Assert.Contains("Confirm below", reply.Answer);
            Assert.NotNull(reply.Action);
            Assert.Equal(("proposed", true), (reply.Action!.Status, reply.Action.CanConfirm));
            Assert.Equal("Move 2 Starbucks transactions to Coffee", reply.Action.Title);
            Assert.Equal(restaurants.FinancialCategoryId, (await Reload(byHand)).CategoryId);
        }

        [Fact]
        public async Task Without_budget_write_the_card_is_view_only()
        {
            var reply = await Service().AskAsync(Me, new AskAssistantRequest("Always put Shell in Gas", null, Today, null, null, CanChange: false));
            Assert.False(reply.Action!.CanConfirm);
        }

        [Fact]
        public async Task An_answer_that_claims_its_done_is_replaced_with_plain_words()
        {
            var reply = await Service().AskAsync(Me, new AskAssistantRequest("Move all my Starbucks to Coffee #done", null, Today, null, null));

            Assert.Equal(AssistantService.ProposedOnly, reply.Answer);
            Assert.NotNull(reply.Action);
        }

        [Fact]
        public void Done_phrasing_only_matters_while_something_is_proposed()
        {
            var draft = new AnswerDraft("answered", "I've moved 2 to Coffee.", [], "", "none", [], [], "abc");
            Assert.True(AnswerChecks.Check(draft, [2m], "q", new HashSet<string>(), new HashSet<string>(), new HashSet<string> { "abc" }).ClaimsDone);
            Assert.False(AnswerChecks.Check(draft, [2m], "q", new HashSet<string>(), new HashSet<string>()).ClaimsDone);
            Assert.False(AnswerChecks.SaysDone("Here's what I'd change. Confirm below to apply it."));
            Assert.True(AnswerChecks.SaysDone("Done! Your target has been updated."));
        }

        [Fact]
        public void An_action_id_from_somewhere_else_is_dropped()
        {
            var draft = new AnswerDraft("answered", "Here's what I'd change.", [], "", "none", [], [], "not-this-one");
            var r = AnswerChecks.Check(draft, [], "q", new HashSet<string>(), new HashSet<string>(), new HashSet<string> { "abc" });
            Assert.Equal("", r.Draft.ActionId);
        }

        [Fact]
        public async Task Cleanup_removes_week_old_actions()
        {
            await MoveStarbucksToCoffee();
            clock.Now = clock.Now.AddDays(8);
            Assert.Equal(1, await Actions().CleanupAsync());
            Assert.Empty(host.Context.AssistantActions);
        }

        private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
        {
            public T CurrentValue => value;
            public T Get(string? name) => value;
            public IDisposable? OnChange(Action<T, string?> listener) => null;
        }

        /// <summary>The real service, except a bulk update fails partway</summary>
        private sealed class FailingTransactions(ITransactionService inner) : ITransactionService
        {
            public Task<int> BulkUpdateAsync(string userId, IReadOnlyCollection<long> ids, int? categoryId, Treatment? treatment, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("The database went away");
            public Task<TransactionSearchResult> SearchAsync(string userId, TransactionSearch search, CancellationToken cancellationToken = default) => inner.SearchAsync(userId, search, cancellationToken);
            public Task<TransactionModel> UpdateAsync(string userId, TransactionUpdate update, CancellationToken cancellationToken = default) => inner.UpdateAsync(userId, update, cancellationToken);
            public Task SetCategoryByPlaidIdAsync(string userId, string plaidTransactionId, int categoryId, CancellationToken cancellationToken = default) => inner.SetCategoryByPlaidIdAsync(userId, plaidTransactionId, categoryId, cancellationToken);
            public Task SetTreatmentByPlaidIdAsync(string userId, string plaidTransactionId, Treatment treatment, CancellationToken cancellationToken = default) => inner.SetTreatmentByPlaidIdAsync(userId, plaidTransactionId, treatment, cancellationToken);
            public Task<List<CategoryRuleModel>> GetRulesAsync(string userId, CancellationToken cancellationToken = default) => inner.GetRulesAsync(userId, cancellationToken);
            public Task<RuleSaveResult> SaveRuleAsync(string userId, CategoryRuleModel rule, bool applyToPast, CancellationToken cancellationToken = default) => inner.SaveRuleAsync(userId, rule, applyToPast, cancellationToken);
            public Task DeleteRuleAsync(string userId, int ruleId, CancellationToken cancellationToken = default) => inner.DeleteRuleAsync(userId, ruleId, cancellationToken);
            public Task<int> PreviewRuleAsync(string userId, CategoryRuleModel rule, CancellationToken cancellationToken = default) => inner.PreviewRuleAsync(userId, rule, cancellationToken);
        }
    }
}
