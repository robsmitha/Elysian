using System.Text.Json;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Assistant;
using Elysian.Application.Features.Assistant.Models;
using Elysian.Application.Features.Financial.Budgeting;
using Elysian.Application.Features.Financial.Categories;
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
    public class SpendingMathTests
    {
        [Fact]
        public void Grocery_months_within_a_few_percent_are_steady()
        {
            var t = SpendingMath.Trend([552m, 512m, 590m, 561m, 586m, 648m]);

            Assert.Equal(574.83m, t.Mean);
            Assert.Equal((512m, 648m), (t.Min, t.Max));
            Assert.Equal("steady", t.Steadiness);
            Assert.InRange(t.TypicalSwingPct, 7m, 7.5m);
        }

        [Fact]
        public void Months_that_swing_widely_are_uneven_and_a_rising_run_goes_up()
        {
            Assert.Equal("uneven", SpendingMath.Trend([100m, 400m, 50m, 380m]).Steadiness);
            var rising = SpendingMath.Trend([100m, 120m, 140m, 160m, 180m]);
            Assert.Equal("up", rising.Direction);
            Assert.Equal("some_variation", rising.Steadiness);
        }

        [Fact]
        public void Two_months_isnt_enough_to_call_it()
        {
            var t = SpendingMath.Trend([100m, 300m]);
            Assert.Equal(("not_enough_data", "flat"), (t.Steadiness, t.Direction));
        }

        [Fact]
        public void Movers_are_the_biggest_changes_first()
        {
            var movers = SpendingMath.Movers(
                new Dictionary<string, decimal> { ["Dining"] = 200, ["Gas"] = 100, ["Gifts"] = 50 },
                new Dictionary<string, decimal> { ["Dining"] = 320, ["Gas"] = 90, ["Travel"] = 400 }, 3);

            Assert.Equal(["Travel", "Dining", "Gifts"], movers.Select(m => m.Name));
            Assert.Null(movers[0].ChangePct);
            Assert.Equal(60m, movers[1].ChangePct);
        }

        [Fact]
        public void Unusual_finds_a_big_charge_a_double_charge_and_a_new_merchant()
        {
            var d = new DateOnly(2026, 10, 1);
            var history = Enumerable.Range(0, 5).Select(i => new SpendRow(i, d.AddDays(i * 7), "shell", "Shell", 40m)).ToList();
            var window = new List<SpendRow>
            {
                new(10, new DateOnly(2026, 11, 2), "shell", "Shell", 130m),
                new(11, new DateOnly(2026, 11, 3), "netflix", "Netflix", 17.99m),
                new(12, new DateOnly(2026, 11, 4), "netflix", "Netflix", 17.99m),
                new(13, new DateOnly(2026, 11, 5), "new place", "New Place", 64m),
            };

            var found = SpendingMath.FindUnusual(history, window);

            Assert.Equal(130m, Assert.Single(found.Large).Row.Amount);
            Assert.Equal("Netflix", Assert.Single(found.Duplicates).First.Merchant);
            Assert.Equal("New Place", Assert.Single(found.NewMerchants).Merchant);
        }
    }

    public class AnswerCheckTests
    {
        private static AnswerDraft Draft(string answer, params AssistantHighlight[] highlights) =>
            new("answered", answer, [.. highlights], "", "none", [], []);

        private static readonly HashSet<string> NoLinks = [];

        [Fact]
        public void Numbers_from_tools_pass_including_rounded_ones()
        {
            var r = AnswerChecks.Check(
                Draft("You’ve spent **$412.85 at Starbucks** over 63 visits since Jan 4, about $41 a month.", new AssistantHighlight("Per visit", "$6.55")),
                [412.85m, 63m, 40.83m, 6.553m], "How much have I spent at Starbucks total?", NoLinks, NoLinks);

            Assert.True(r.Passed, string.Join(", ", r.UnsupportedNumbers));
        }

        [Fact]
        public void An_invented_number_is_caught()
        {
            var r = AnswerChecks.Check(Draft("November is on pace for $917."), [214m, 575m], "Is it steady?", NoLinks, NoLinks);
            Assert.Equal(["$917"], r.UnsupportedNumbers);
        }

        [Fact]
        public void Dates_years_small_counts_and_numbers_from_the_question_are_fine()
        {
            var r = AnswerChecks.Check(Draft("Between Oct 21, 2026 and 2026-11-07 you made 3 trips, and $100 is your limit."), [], "Anything over $100?", NoLinks, NoLinks);
            Assert.True(r.Passed, string.Join(", ", r.UnsupportedNumbers));
        }

        [Fact]
        public void Percentages_may_be_rounded()
        {
            var r = AnswerChecks.Check(Draft("October was 13% above average, a 7.2% typical swing."), [12.7m, 7.24m], "q", NoLinks, NoLinks);
            Assert.True(r.Passed);
            Assert.False(AnswerChecks.Check(Draft("Up 40%."), [12.7m], "q", NoLinks, NoLinks).Passed);
        }

        [Fact]
        public void Links_and_charts_not_from_a_tool_are_dropped()
        {
            var draft = new AnswerDraft("answered", "Here.", [], "toolu_unknown", "bar",
                [new("Mine", "/transactions?month=2026-10&category=12"), new("Elsewhere", "https://evil.example"), new("Made up", "/transactions?month=2020-01")], []);

            var r = AnswerChecks.Check(draft, [], "q", new HashSet<string> { "/transactions?month=2026-10&category=12", "https://evil.example" }, new HashSet<string> { "toolu_1" });

            Assert.Equal(["Mine"], r.Draft.Links.Select(l => l.Label));
            Assert.Equal(("none", ""), (r.Draft.ChartKind, r.Draft.ChartToolCallId));
        }

        [Fact]
        public void Only_the_agreed_shape_parses()
        {
            Assert.Null(AnswerChecks.Parse("not json"));
            Assert.Null(AnswerChecks.Parse("""{ "status": "maybe", "answer": "x" }"""));
            var d = AnswerChecks.Parse("""
                { "status": "answered", "answer": "Hi", "highlights": [{ "label": "A", "value": "1" }, { "label": "B", "value": "2" }, { "label": "C", "value": "3" }, { "label": "D", "value": "4" }],
                  "chart_kind": "pie", "chart_tool_call_id": "", "links": [], "follow_ups": ["Next?"] }
                """);
            Assert.NotNull(d);
            Assert.Equal(3, d!.Highlights.Count);
            Assert.Equal("none", d.ChartKind);
        }
    }

    /// <summary>The tools and the whole flow, on an in-memory database with two people and the offline model</summary>
    public sealed class AssistantServiceTests : IDisposable
    {
        private const string Other = "other-swa-id";
        private readonly TestHost host = new();
        private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero));
        private static readonly DateOnly Today = new(2026, 11, 7);
        private readonly AssistantSettings settings = new() { Enabled = true, PerDay = 30, PerMinute = 100 };
        private readonly FinancialAccount checking, othersChecking;
        private readonly FinancialCategory groceries, coffee, othersCategory;
        private readonly AssistantTools tools;
        private long nextId = 1;

        public AssistantServiceTests()
        {
            var protector = Protector(Settings());
            var mine = SeedBank(host, protector).Item;
            var theirs = SeedBank(host, protector, "access-sandbox-2", Other, 2, "item-2").Item;
            checking = new FinancialAccount { PlaidAccountId = "chk", Name = "Checking", Mask = "4410", Type = "depository", InstitutionAccessItemId = mine.InstitutionAccessItemId, BudgetRole = AccountBudgetRole.Spending };
            othersChecking = new FinancialAccount { PlaidAccountId = "chk2", Name = "Their checking", Mask = "9999", Type = "depository", InstitutionAccessItemId = theirs.InstitutionAccessItemId, BudgetRole = AccountBudgetRole.Spending };
            host.Context.FinancialAccounts.AddRange(checking, othersChecking);
            groceries = new FinancialCategory { Name = "Groceries", UserId = Me, Group = CategoryGroup.Flexible, SystemKey = "groceries" };
            coffee = new FinancialCategory { Name = "Coffee", UserId = Me, Group = CategoryGroup.Flexible, SystemKey = "coffee" };
            othersCategory = new FinancialCategory { Name = "Groceries", UserId = Other, Group = CategoryGroup.Flexible, SystemKey = "groceries" };
            host.Context.FinancialCategories.AddRange(groceries, coffee, othersCategory);
            host.Context.SaveChanges();

            // Mine: groceries most months, Starbucks now and then; theirs: a big Starbucks habit
            decimal[] grocery = [552, 512, 590, 561, 586, 648];
            for (var i = 0; i < 6; i++) Spend(checking, new DateOnly(2026, 5 + i, 10), grocery[i], groceries, "Trader Joe's");
            Spend(checking, new DateOnly(2026, 11, 3), 214, groceries, "Trader Joe's");
            Spend(checking, new DateOnly(2026, 9, 2), 6.50m, coffee, "Starbucks");
            Spend(checking, new DateOnly(2026, 11, 2), 7.25m, coffee, "STARBUCKS STORE 1234");
            Spend(checking, new DateOnly(2026, 10, 15), 2500, null, "Transfer to savings", Treatment.Transfer);
            Spend(othersChecking, new DateOnly(2026, 11, 1), 999, othersCategory, "Starbucks");
            host.Context.SaveChanges();

            var categories = new CategoryService(host.Context, Financial.PlaidServiceTests.Mapper);
            var classifier = new TransactionClassifier(host.Context, categories, clock);
            tools = new AssistantTools(host.Context, new BudgetMonthService(host.Context, classifier, clock), new GoalService(host.Context, clock), new DebtService(host.Context, clock),
                new TransactionService(host.Context, classifier), clock);
        }

        public void Dispose() => host.Dispose();

        private void Spend(FinancialAccount account, DateOnly date, decimal amount, FinancialCategory? category, string name, Treatment treatment = Treatment.Spending)
        {
            host.Context.BankTransactions.Add(new BankTransaction
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
                CategorySource = ClassificationSource.User,
                Treatment = treatment,
                TreatmentSource = ClassificationSource.User,
                ClassifiedAt = clock.GetUtcNow(),
            });
        }

        private Task<ToolOutcome> Run(string tool, string input, string user = Me) =>
            tools.RunAsync(user, Today, new ModelToolCall("toolu_1", tool, JsonDocument.Parse(input).RootElement), CancellationToken.None);

        private AssistantService Service(IAssistantModel? model = null) =>
            new(host.Context, new Monitor<AssistantSettings>(settings), new AssistantLimiter(host.Context, new Monitor<AssistantSettings>(settings), clock), tools,
                model ?? new FakeAssistantModel(), new TransactionClassifier(host.Context, new CategoryService(host.Context, Financial.PlaidServiceTests.Mapper), clock),
                clock, NullLogger<AssistantService>.Instance);

        private static AskAssistantRequest Ask(string question) => new(question, null, Today, "budget", "2026-11");

        [Fact]
        public async Task A_merchant_total_counts_only_my_own_transactions_and_matches_the_bank_text()
        {
            var r = await Run("find_transactions", """{ "merchant": "starbucks" }""");
            using var doc = JsonDocument.Parse(r.Json);

            Assert.False(r.IsError);
            Assert.Equal(2, doc.RootElement.GetProperty("match_count").GetInt32());
            Assert.Equal(13.75m, doc.RootElement.GetProperty("total_out").GetDecimal());
            Assert.Equal("/transactions?from=2025-12-01&to=2026-11-07&q=starbucks&view=spending", doc.RootElement.GetProperty("link").GetString());
        }

        [Fact]
        public async Task Someone_elses_category_or_account_is_refused()
        {
            var category = await Run("summarize_spending", $$"""{ "group_by": "month", "category_ids": [{{othersCategory.FinancialCategoryId}}] }""");
            var account = await Run("find_transactions", $$"""{ "account_ids": [{{othersChecking.FinancialAccountId}}] }""");

            Assert.True(category.IsError);
            Assert.Contains("Unknown category", category.Json);
            Assert.True(account.IsError);
        }

        [Fact]
        public async Task The_grocery_trend_is_steady_and_compares_this_month_with_the_same_day_last_month()
        {
            var r = await Run("spending_trend", $$"""{ "category_id": {{groceries.FinancialCategoryId}} }""");
            using var doc = JsonDocument.Parse(r.Json);
            var full = doc.RootElement.GetProperty("full_months");

            Assert.Equal("steady", full.GetProperty("steadiness").GetString());
            Assert.Equal(574.83m, full.GetProperty("mean").GetDecimal());
            Assert.Equal(214m, doc.RootElement.GetProperty("this_month").GetProperty("so_far").GetDecimal());
            Assert.NotNull(r.Chart);
            Assert.Equal(7, r.Chart!.Values.Count);
            Assert.True(r.Chart.LastIsPartial);
        }

        [Fact]
        public async Task Transfers_arent_counted_unless_asked_for()
        {
            var counted = await Run("summarize_spending", """{ "group_by": "category", "from": "2026-10-01", "to": "2026-10-31" }""");
            var transfers = await Run("summarize_spending", """{ "group_by": "category", "from": "2026-10-01", "to": "2026-10-31", "include": "transfers" }""");

            Assert.Equal(648m, JsonDocument.Parse(counted.Json).RootElement.GetProperty("total").GetDecimal());
            Assert.Equal(2500m, JsonDocument.Parse(transfers.Json).RootElement.GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task A_bad_date_comes_back_as_an_error_the_model_can_fix()
        {
            var r = await Run("find_transactions", """{ "from": "last tuesday" }""");
            Assert.True(r.IsError);
            Assert.Contains("must be a date", r.Json);
        }

        [Fact]
        public async Task A_question_is_answered_offline_with_steps_a_link_and_a_question_used()
        {
            var reply = await Service().AskAsync(Me, Ask("How much have I spent at Starbucks total?"));

            Assert.Equal("answered", reply.Status);
            Assert.StartsWith("[sample]", reply.Answer);
            Assert.Contains(reply.Steps, s => s.Contains("starbucks", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("/transactions?from=2025-12-01&to=2026-11-07&q=Starbucks&view=spending", Assert.Single(reply.Links).To);
            Assert.Equal((1, 29), (reply.Usage.UsedToday, reply.Usage.RemainingToday));

            var row = await host.Context.AssistantUsages.SingleAsync();
            Assert.Equal((AssistantOutcome.Answered, Me, 1), (row.Outcome, row.UserId, row.ToolCalls));
        }

        [Fact]
        public async Task A_trend_question_gets_a_chart_from_the_tool_not_the_model()
        {
            var reply = await Service().AskAsync(Me, Ask("Is my groceries spending pretty steady?"));

            Assert.Equal("answered", reply.Status);
            Assert.NotNull(reply.Chart);
            Assert.Equal(new[] { 552m, 512m, 590m, 561m, 586m, 648m, 214m }, reply.Chart!.Values);
        }

        [Fact]
        public async Task An_answer_with_an_invented_number_falls_back_and_gives_the_question_back()
        {
            var reply = await Service().AskAsync(Me, Ask("How much at Starbucks? #bad"));

            Assert.Equal("fallback", reply.Status);
            Assert.Equal(AssistantService.Fallback, reply.Answer);
            Assert.Equal(0, reply.Usage.UsedToday);
            Assert.Equal(AssistantOutcome.Fallback, (await host.Context.AssistantUsages.SingleAsync()).Outcome);
        }

        [Fact]
        public async Task A_refusal_is_out_of_scope_and_not_counted()
        {
            var reply = await Service().AskAsync(Me, Ask("Should I buy index funds? #refuse"));

            Assert.Equal("out_of_scope", reply.Status);
            Assert.Equal(0, reply.Usage.UsedToday);
        }

        [Fact]
        public async Task The_daily_limit_stops_the_next_question_before_the_model_is_called()
        {
            settings.PerDay = 2;
            var service = Service();
            await service.AskAsync(Me, Ask("Starbucks total?"));
            await service.AskAsync(Me, Ask("Starbucks total?"));

            var ex = await Assert.ThrowsAsync<TooManyRequestsException>(() => service.AskAsync(Me, Ask("Starbucks total?")));
            Assert.Contains("today's 2 questions", ex.Message);
            Assert.Equal(2, await host.Context.AssistantUsages.CountAsync());

            // Someone else still has theirs
            Assert.Equal(2, (await service.GetUsageAsync(Other, Today)).RemainingToday);
        }

        [Fact]
        public async Task Too_many_at_once_is_slowed_down()
        {
            settings.PerMinute = 1;
            var service = Service();
            await service.AskAsync(Me, Ask("Starbucks total?"));
            await Assert.ThrowsAsync<TooManyRequestsException>(() => service.AskAsync(Me, Ask("Starbucks total?")));
        }

        [Fact]
        public async Task Switched_off_it_refuses()
        {
            settings.Enabled = false;
            await Assert.ThrowsAsync<NotFoundException>(() => Service().AskAsync(Me, Ask("Starbucks total?")));
        }

        [Fact]
        public async Task The_setting_is_read_on_every_question_not_once()
        {
            var service = Service();
            settings.Enabled = false;
            await Assert.ThrowsAsync<NotFoundException>(() => service.AskAsync(Me, Ask("Starbucks total?")));

            settings.Enabled = true;
            Assert.Equal("answered", (await service.AskAsync(Me, Ask("Starbucks total?"))).Status);
            Assert.Equal("answered", (await service.AskAsync(Me, Ask("And last month?"))).Status);
        }

        [Fact]
        public async Task Running_out_of_time_is_a_fallback()
        {
            settings.TotalSeconds = 1;
            var reply = await Service().AskAsync(Me, Ask("Starbucks total? #slow"));

            Assert.Equal("fallback", reply.Status);
            Assert.Equal(AssistantService.TooSlow, reply.Answer);
        }

        [Fact]
        public async Task Earlier_turns_go_back_as_plain_text_in_order_and_the_context_comes_last()
        {
            var model = new RecordingModel();
            await Service(model).AskAsync(Me, new AskAssistantRequest("And last month?",
                [new("assistant", "stray"), new("user", "Starbucks total?"), new("assistant", "$13.75."), new("user", "dangling")], Today, null, null));

            var sent = model.Requests[0].Messages;
            Assert.Equal(3, sent.Count);
            Assert.Equal("Starbucks total?", Assert.IsType<UserText>(sent[0]).Text);
            Assert.Equal("$13.75.", Assert.IsType<AssistantText>(sent[1]).Text);
            var context = Assert.IsType<UserText>(sent[2]).Text;
            Assert.Contains("Today is 2026-11-07 (Saturday).", context);
            Assert.Contains($"Groceries (Flexible, id {groceries.FinancialCategoryId})", context);
            Assert.DoesNotContain("Their checking", context);
            Assert.EndsWith("Question: And last month?", context);
        }

        [Fact]
        public void The_request_to_claude_opts_into_fallbacks_caching_effort_and_the_answer_format()
        {
            var p = AnthropicAssistantModel.Build(
                new ModelRequest(AssistantPrompt.System, AssistantTools.Definitions, [new UserText("Question: hi")], AssistantPrompt.AnswerSchema),
                new AssistantSettings { Model = "claude-opus-5-5", Effort = "low" });
            var json = JsonSerializer.Serialize(p);

            Assert.Contains("\"fallbacks\":\"default\"", json);
            Assert.Contains(AnthropicAssistantModel.FallbackBeta, json);
            Assert.Contains("\"effort\":\"low\"", json);
            Assert.Contains("\"cache_control\":{\"type\":\"ephemeral\"}", json);
            Assert.Contains("\"name\":\"spending_trend\"", json);
            Assert.Contains("\"json_schema\"", json);
            Assert.DoesNotContain("temperature", json);
        }

        /// <summary>Settings the test can change between calls, as app settings can change between requests</summary>
        private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
        {
            public T CurrentValue => value;
            public T Get(string? name) => value;
            public IDisposable? OnChange(Action<T, string?> listener) => null;
        }

        /// <summary>Answers straight away without tools, and keeps what it was sent</summary>
        private sealed class RecordingModel : IAssistantModel
        {
            public List<ModelRequest> Requests { get; } = [];

            public Task<ModelResponse> CreateAsync(ModelRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request with { Messages = [.. request.Messages] });
                return Task.FromResult(new ModelResponse(ModelStop.EndTurn,
                    """{ "status": "answered", "answer": "Fine.", "highlights": [], "chart_kind": "none", "chart_tool_call_id": "", "links": [], "follow_ups": [] }""",
                    [], new ModelUsage(10, 5, 0, 0), "recording", null));
            }
        }
    }
}
