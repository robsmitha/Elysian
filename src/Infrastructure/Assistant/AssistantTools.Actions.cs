using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elysian.Application.Features.Assistant.Models;
using Elysian.Application.Features.Financial.Budgeting;
using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Assistant
{
    /// <summary>
    /// The exact change a proposal makes (AssistantAction.ChangeJson). One shape for every kind; each kind uses its own
    /// fields.
    /// </summary>
    public record ActionChange
    {
        public List<long> Ids { get; init; } = [];
        public int? CategoryId { get; init; }
        public string? CategoryName { get; init; }
        public Treatment? Treatment { get; init; }
        /// <summary>The rule a Rule action saves, or the one the card's tick box would add</summary>
        public CategoryRuleModel? Rule { get; init; }
        public bool RuleTicked { get; init; }
        public bool ApplyToPast { get; init; }
        public int? Year { get; init; }
        public int? Month { get; init; }
        public decimal? Amount { get; init; }
        public bool Rollover { get; init; }
        /// <summary>Category: add, rename or merge</summary>
        public string? Op { get; init; }
        public string? Name { get; init; }
        public CategoryGroup? Group { get; init; }
        public int? IntoCategoryId { get; init; }
    }

    public record RowBefore(long Id, int? CategoryId, ClassificationSource CategorySource, Treatment Treatment, ClassificationSource TreatmentSource);

    /// <summary>Values before the change (AssistantAction.BeforeJson), for undo</summary>
    public record ActionBefore
    {
        public List<RowBefore> Rows { get; init; } = [];
        public decimal? TargetAmount { get; init; }
        public bool TargetRollover { get; init; }
        public string? CategoryName { get; init; }
    }

    /// <summary>What applying created (AssistantAction.AppliedJson)</summary>
    public record ActionApplied(int? RuleId, int? CategoryId);

    /// <summary>A worked-out proposal, before it's saved</summary>
    public record Proposal(AssistantActionKind Kind, ActionChange Change, ActionBefore Before, string Fingerprint, string Summary,
        int Affected, bool CanUndo, AssistantActionModel Card, object ForModel);

    public static class ActionJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
        public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
    }

    /// <summary>
    /// The propose_* tools (docs/assistant-actions-plan.md §2). Like the other tools they only read: each works out the
    /// exact change for this person's own data and saves it as a proposal. Nothing changes until the person confirms
    /// through <see cref="AssistantActions"/>, which Claude can't call.
    /// </summary>
    public partial class AssistantTools
    {
        public const int MaxActionRows = 500;
        public static readonly TimeSpan ProposalLifetime = TimeSpan.FromMinutes(15);
        public const int CardRows = 5;

        private const string NothingChanged = "Nothing has changed. The person sees a card with exactly this change and decides whether to confirm it.";

        private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

        private static IReadOnlyList<ToolDefinition> ActionDefinitions() =>
        [
            Def("propose_recategorize",
                "Proposes moving transactions to another category. Nothing changes: the person sees a card listing exactly what would move and taps Confirm. Choose transactions by merchant and/or the categories they're in now, over a date range. Counted transactions only; split transactions are left alone. Returns action_id, the count, total, dates and examples.",
                """
                "merchant": { "type": "string", "description": "Merchant or description text, e.g. \"starbucks\"." },
                "category_ids": { "type": "array", "items": { "type": "integer" }, "description": "Only transactions now in these categories (ids from the context; 0 means no category)." },
                "from": { "type": "string", "description": "First day, YYYY-MM-DD. Default: the first of the month 11 months ago. For \"all\", use three years before today." },
                "to": { "type": "string", "description": "Last day, YYYY-MM-DD. Default: today." },
                "to_category_id": { "type": "integer", "description": "The category to move them to, an id from the context." },
                "include_rule": { "type": "boolean", "description": "true when the person said always, from now on or every time, so future charges from this merchant go there too." }
                """, "\"to_category_id\""),
            Def("propose_set_treatment",
                "Proposes changing how transactions count: as Spending, Refund, Income, Transfer (between the person's own accounts), CardPayment, or Hidden (left out of budgets). Nothing changes until the person confirms the card. Choose transactions by merchant, current categories or accounts, over a date range. Returns action_id, the count, total and examples.",
                """
                "treatment": { "type": "string", "enum": ["Spending", "Refund", "Income", "Transfer", "CardPayment", "Hidden"] },
                "merchant": { "type": "string", "description": "Merchant or description text, e.g. \"venmo\"." },
                "category_ids": { "type": "array", "items": { "type": "integer" }, "description": "Only transactions now in these categories." },
                "account_ids": { "type": "array", "items": { "type": "integer" }, "description": "Only these accounts (ids from list_accounts_and_categories)." },
                "from": { "type": "string", "description": "First day, YYYY-MM-DD. Default: the first of the month 11 months ago." },
                "to": { "type": "string", "description": "Last day, YYYY-MM-DD. Default: today." },
                "include_rule": { "type": "boolean", "description": "true when the person said always or from now on." }
                """, "\"treatment\""),
            Def("propose_rule",
                "Proposes a rule that places future transactions automatically, like \"Merchant is Shell → Gas\". It needs a category, a treatment, or both. With apply_to_past (default true) it also re-places past transactions the person didn't set by hand. Nothing changes until the person confirms. Returns action_id and how many past transactions it would change.",
                """
                "match_text": { "type": "string", "description": "What to match, e.g. \"shell\"." },
                "field": { "type": "string", "enum": ["merchant", "description"], "description": "merchant (default) matches the cleaned-up merchant name; description the bank's text." },
                "match": { "type": "string", "enum": ["contains", "equals"], "description": "Default contains." },
                "direction": { "type": "string", "enum": ["out", "in", "any"], "description": "out (default) for charges, in for money coming in." },
                "set_category_id": { "type": "integer", "description": "The category to put them in." },
                "set_treatment": { "type": "string", "enum": ["Spending", "Refund", "Income", "Transfer", "CardPayment", "Hidden"] },
                "apply_to_past": { "type": "boolean", "description": "Default true." }
                """, "\"match_text\""),
            Def("propose_target",
                "Proposes a category's monthly budget target: a new amount, or a change up or down. Returns the target now and after, and what it does to not yet assigned. Nothing changes until the person confirms. Default month: this one.",
                """
                "category_id": { "type": "integer", "description": "An id from the context." },
                "year": { "type": "integer" },
                "month": { "type": "integer", "description": "1 to 12" },
                "amount": { "type": "number", "description": "The new target." },
                "change": { "type": "number", "description": "Instead of amount: how much to raise it (negative lowers it)." }
                """, "\"category_id\""),
            Def("propose_category",
                "Proposes category housekeeping: add a new category, rename one, or merge one into another (its transactions, rules and targets move, then it's archived; a merge can't be undone). Nothing changes until the person confirms.",
                """
                "op": { "type": "string", "enum": ["add", "rename", "merge"] },
                "name": { "type": "string", "description": "add: the new category's name. rename: the new name." },
                "group": { "type": "string", "enum": ["Fixed", "Flexible", "NonMonthly"], "description": "add only. Default Flexible." },
                "category_id": { "type": "integer", "description": "rename: the category to rename. merge: the one that goes away." },
                "into_category_id": { "type": "integer", "description": "merge: the category that stays." }
                """, "\"op\""),
        ];

        public static readonly HashSet<string> ProposeTools =
            ["propose_recategorize", "propose_set_treatment", "propose_rule", "propose_target", "propose_category"];

        private static AssistantActionKind KindOf(string tool) => tool switch
        {
            "propose_recategorize" => AssistantActionKind.Recategorize,
            "propose_set_treatment" => AssistantActionKind.SetTreatment,
            "propose_rule" => AssistantActionKind.Rule,
            "propose_target" => AssistantActionKind.Target,
            "propose_category" => AssistantActionKind.Category,
            _ => throw new ToolInputException($"There's no tool called {tool}.")
        };

        /// <summary>What a proposal was built from, so confirming can build it again and see whether anything moved</summary>
        private record ActionRequest(string Tool, DateOnly Today, JsonElement Input);

        /// <summary>Works out a propose_* call and saves it as a proposal. Returns the model's result and the card.</summary>
        private async Task<(object, string, AssistantActionModel)> ProposeAsync(string userId, DateOnly today, ModelToolCall call, CancellationToken ct)
        {
            var proposal = await BuildAsync(userId, today, call.Name, new ToolInput(call.Input), ct);
            var now = clock.GetUtcNow();
            var action = new AssistantAction
            {
                AssistantActionId = Guid.NewGuid(),
                UserId = userId,
                Kind = proposal.Kind,
                Status = AssistantActionStatus.Proposed,
                RequestJson = ActionJson.Write(new ActionRequest(call.Name, today, call.Input)),
                CreatedAt = now,
            };
            Fill(action, proposal, now);
            context.AssistantActions.Add(action);
            await context.SaveChangesAsync(ct);

            var card = ActionJson.Read<AssistantActionModel>(action.CardJson);
            var forModel = JsonSerializer.SerializeToElement(proposal.ForModel, Json);
            var result = new Dictionary<string, object> { ["action_id"] = action.AssistantActionId.ToString() };
            foreach (var p in forModel.EnumerateObject()) result[p.Name] = p.Value;
            result["note"] = NothingChanged;
            return (result, $"Worked out a change to confirm: {proposal.Summary}", card);
        }

        /// <summary>Puts a (fresh) proposal on the saved action, with a new 15 minutes to confirm</summary>
        public static void Fill(AssistantAction action, Proposal proposal, DateTimeOffset now)
        {
            action.ChangeJson = ActionJson.Write(proposal.Change);
            action.BeforeJson = ActionJson.Write(proposal.Before);
            action.Fingerprint = proposal.Fingerprint;
            action.Summary = proposal.Summary.Length > 200 ? proposal.Summary[..200] : proposal.Summary;
            action.AffectedCount = proposal.Affected;
            action.CanUndo = proposal.CanUndo;
            action.ExpiresAt = now + ProposalLifetime;
            action.CardJson = ActionJson.Write(proposal.Card with { Id = action.AssistantActionId, ExpiresAt = action.ExpiresAt });
        }

        /// <summary>The proposal a saved action was built from, worked out again from today's data</summary>
        public async Task<Proposal> RebuildAsync(string userId, AssistantAction action, CancellationToken ct)
        {
            var request = ActionJson.Read<ActionRequest>(action.RequestJson);
            return await BuildAsync(userId, request.Today, request.Tool, new ToolInput(request.Input), ct);
        }

        private Task<Proposal> BuildAsync(string userId, DateOnly today, string tool, ToolInput input, CancellationToken ct) =>
            KindOf(tool) switch
            {
                AssistantActionKind.Recategorize => RecategorizeAsync(userId, today, input, ct),
                AssistantActionKind.SetTreatment => TreatmentAsync(userId, today, input, ct),
                AssistantActionKind.Rule => RuleAsync(userId, input, ct),
                AssistantActionKind.Target => TargetAsync(userId, today, input, ct),
                _ => CategoryAsync(userId, input, ct),
            };

        // ---- recategorize and treatment ----

        private async Task<Proposal> RecategorizeAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            var cats = await CategoriesAsync(userId, ct);
            var toId = input.Int("to_category_id") ?? throw new ToolInputException("to_category_id is required.");
            if (!cats.TryGetValue(toId, out var target) || target.IsArchived)
            {
                throw new ToolInputException($"Unknown category id {toId}. Use an id from the context, or propose_category to make it first.");
            }
            var f = await ReadFilterAsync(userId, today, input, ct) with { Include = "counted" };
            RequireNarrow(f);
            var matched = await RowsAsync(userId, f, ct);
            var splits = matched.Count(r => r.Splits.Count > 0);
            var whole = matched.Where(r => r.Splits.Count == 0).ToList();
            var already = whole.Count(r => r.CategoryId == toId);
            var rows = whole.Where(r => r.CategoryId != toId).OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList();
            if (rows.Count == 0)
            {
                throw new ToolInputException(already > 0
                    ? $"Nothing to change: all {already} matching transactions are already in {target.Name}."
                    : $"No counted transactions match {Describe(f, cats)}.");
            }
            CheckSize(rows.Count);

            var before = await BeforeAsync(userId, rows, ct);
            var label = Label(f, rows, cats);
            var byHand = before.Count(b => b.CategorySource == ClassificationSource.User);
            var warnings = new List<string>();
            if (byHand > 0) warnings.Add($"{Plural(byHand, "of these")} you set by hand. They’ll move too.");
            if (splits > 0) warnings.Add($"{Plural(splits, "split transaction")} {(splits == 1 ? "is" : "are")} left as {(splits == 1 ? "it is" : "they are")}.");
            var rule = f.Merchant != null ? MerchantRule(f.Merchant, toId, null) : null;
            var ticked = rule != null && input.Bool("include_rule") == true;

            var summary = $"Move {Plural(rows.Count, label + " transaction")} to {target.Name}";
            var total = rows.Sum(r => r.Amount);
            var card = Card(AssistantActionKind.Recategorize, summary, Subtitle(rows, total, already > 0 ? $"{already} already in {target.Name}" : null),
                rows.Take(CardRows).Select(r => new AssistantActionRow(r.Date, r.Merchant, r.Amount, CategoryName(r.CategoryId, cats), target.Name)).ToList(),
                rows.Count, warnings, rule != null ? new AssistantActionRuleOffer($"Also do this for future {label} charges", ticked) : null,
                new AssistantLink("See them in Transactions", TransactionsLink(f.From, f.To, toId, f.Merchant)), ["transactions", "budget", "rules"]);

            return new Proposal(AssistantActionKind.Recategorize,
                new ActionChange { Ids = rows.Select(r => r.Id).ToList(), CategoryId = toId, CategoryName = target.Name, Rule = rule, RuleTicked = ticked },
                new ActionBefore { Rows = before }, Hash(before), summary, rows.Count, true, card,
                new
                {
                    summary,
                    transactions = rows.Count,
                    total = SpendingMath.Round(total),
                    from = rows.Min(r => r.Date),
                    to = rows.Max(r => r.Date),
                    examples = Examples(rows, cats),
                    set_by_hand = byHand,
                    already_in_category = already,
                    split_transactions_left_alone = splits,
                    rule_for_future_charges = ticked,
                });
        }

        private async Task<Proposal> TreatmentAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            var treatment = ParseTreatment(input.Str("treatment")) ?? throw new ToolInputException("treatment is required.");
            var cats = await CategoriesAsync(userId, ct);
            var f = await ReadFilterAsync(userId, today, input, ct) with { Include = "all" };
            RequireNarrow(f);
            var matched = await RowsAsync(userId, f, ct);
            var already = matched.Count(r => r.Treatment == treatment);
            var rows = matched.Where(r => r.Treatment != treatment).OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList();
            if (rows.Count == 0)
            {
                throw new ToolInputException(already > 0
                    ? $"Nothing to change: all {already} matching transactions already count as {treatment}."
                    : $"No transactions match {Describe(f, cats)}.");
            }
            CheckSize(rows.Count);

            var before = await BeforeAsync(userId, rows, ct);
            var label = Label(f, rows, cats);
            var byHand = before.Count(b => b.TreatmentSource == ClassificationSource.User);
            var warnings = byHand > 0 ? new List<string> { $"{Plural(byHand, "of these")} you set by hand. They’ll change too." } : [];
            var rule = f.Merchant != null ? MerchantRule(f.Merchant, null, treatment) : null;
            var ticked = rule != null && input.Bool("include_rule") == true;

            var summary = treatment == Treatment.Hidden
                ? $"Hide {Plural(rows.Count, label + " transaction")}"
                : $"Count {Plural(rows.Count, label + " transaction")} as {TreatmentWords(treatment)}";
            var total = rows.Sum(r => r.Amount);
            var card = Card(AssistantActionKind.SetTreatment, summary, Subtitle(rows, total, already > 0 ? $"{already} already {TreatmentWords(treatment)}" : null),
                rows.Take(CardRows).Select(r => new AssistantActionRow(r.Date, r.Merchant, r.Amount, TreatmentName(r.Treatment), TreatmentName(treatment))).ToList(),
                rows.Count, warnings, rule != null ? new AssistantActionRuleOffer($"Also do this for future {label} transactions", ticked) : null,
                new AssistantLink("See them in Transactions", TransactionsLink(f.From, f.To, null, f.Merchant, "all")), ["transactions", "budget", "rules"]);

            return new Proposal(AssistantActionKind.SetTreatment,
                new ActionChange { Ids = rows.Select(r => r.Id).ToList(), Treatment = treatment, Rule = rule, RuleTicked = ticked },
                new ActionBefore { Rows = before }, Hash(before), summary, rows.Count, true, card,
                new
                {
                    summary,
                    transactions = rows.Count,
                    total = SpendingMath.Round(total),
                    from = rows.Min(r => r.Date),
                    to = rows.Max(r => r.Date),
                    examples = Examples(rows, cats),
                    set_by_hand = byHand,
                    already_counted_that_way = already,
                    rule_for_future_transactions = ticked,
                });
        }

        private static void RequireNarrow(Filter f)
        {
            if (f.Merchant == null && f.CategoryIds == null && f.AccountIds == null)
            {
                throw new ToolInputException("Say which transactions: a merchant, the categories they're in now, or an account.");
            }
        }

        private static void CheckSize(int count)
        {
            if (count > MaxActionRows)
            {
                throw new ToolInputException($"That would change {count} transactions; one change can cover at most {MaxActionRows}. Use a shorter date range.");
            }
        }

        private async Task<List<RowBefore>> BeforeAsync(string userId, List<Row> rows, CancellationToken ct)
        {
            var ids = rows.Select(r => r.Id).ToList();
            return (await Mine(userId).Where(t => ids.Contains(t.BankTransactionId))
                    .Select(t => new RowBefore(t.BankTransactionId, t.CategoryId, t.CategorySource, t.Treatment, t.TreatmentSource))
                    .ToListAsync(ct))
                .OrderBy(b => b.Id).ToList();
        }

        /// <summary>"Starbucks" (the merchant name seen most; on a tie the shorter, cleaner one), or the categories they’re in now</summary>
        private static string Label(Filter f, List<Row> rows, Dictionary<int, FinancialCategory> cats)
        {
            if (f.Merchant != null) return rows.GroupBy(r => r.Merchant).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Length).First().Key;
            if (f.CategoryIds != null) return string.Join(" and ", f.CategoryIds.Select(id => CategoryName(id == 0 ? null : id, cats)));
            return "";
        }

        private static CategoryRuleModel MerchantRule(string merchant, int? categoryId, Treatment? treatment) => new()
        {
            Field = RuleField.Merchant,
            Match = RuleMatch.Contains,
            Value = merchant,
            Direction = RuleDirection.Out,
            SetCategoryId = categoryId,
            SetTreatment = treatment,
        };

        private static object Examples(List<Row> rows, Dictionary<int, FinancialCategory> cats) =>
            rows.Take(CardRows).Select(r => new
            {
                date = r.Date,
                merchant = r.Merchant,
                amount = r.Amount,
                category_now = CategoryName(r.CategoryId, cats),
                counts_as_now = r.Treatment.ToString(),
            }).ToList();

        private static string Subtitle(List<Row> rows, decimal total, string? extra)
        {
            var from = rows.Min(r => r.Date);
            var to = rows.Max(r => r.Date);
            var dates = from == to ? Day(from, true) : $"{Day(from, from.Year != to.Year)} – {Day(to, true)}";
            return extra == null ? $"{dates} · {Money(total)}" : $"{dates} · {Money(total)} · {extra}";
        }

        // ---- rules ----

        private async Task<Proposal> RuleAsync(string userId, ToolInput input, CancellationToken ct)
        {
            var text = Clean(input.Str("match_text")) ?? throw new ToolInputException("match_text is required.");
            var field = (input.Str("field") ?? "merchant") switch
            {
                "merchant" => RuleField.Merchant,
                "description" => RuleField.Description,
                var other => throw new ToolInputException($"field must be merchant or description, not {other}.")
            };
            var match = (input.Str("match") ?? "contains") == "equals" ? RuleMatch.Equals : RuleMatch.Contains;
            var direction = (input.Str("direction") ?? "out") switch { "in" => RuleDirection.In, "any" => RuleDirection.Any, _ => RuleDirection.Out };
            var cats = await CategoriesAsync(userId, ct);
            var categoryId = input.Int("set_category_id");
            if (categoryId is int id && (!cats.TryGetValue(id, out var c) || c.IsArchived))
            {
                throw new ToolInputException($"Unknown category id {id}. Use an id from the context, or propose_category to make it first.");
            }
            var treatment = ParseTreatment(input.Str("set_treatment"));
            if (categoryId == null && treatment == null) throw new ToolInputException("A rule needs set_category_id, set_treatment, or both.");
            var applyToPast = input.Bool("apply_to_past") ?? true;

            var rule = new CategoryRuleModel { Field = field, Match = match, Value = text, Direction = direction, SetCategoryId = categoryId, SetTreatment = treatment };
            var past = await transactions.PreviewRuleAsync(userId, rule, ct);
            var existing = (await transactions.GetRulesAsync(userId, ct))
                .Where(r => r.Field == field && string.Equals(r.Value.Trim(), text, StringComparison.OrdinalIgnoreCase)).ToList();

            var sets = string.Join(", ", new[] { categoryId is int cid ? CategoryName(cid, cats) : null, treatment is Treatment t ? TreatmentName(t) : null }.Where(s => s != null));
            var summary = $"New rule: {(field == RuleField.Merchant ? "Merchant" : "Description")} {(match == RuleMatch.Equals ? "is" : "contains")} “{text}” → {sets}";
            var facts = new List<AssistantHighlight>();
            string subtitle;
            if (applyToPast)
            {
                facts.Add(new AssistantHighlight("Past transactions it changes", past.ToString(CultureInfo.InvariantCulture)));
                subtitle = past == 0 ? "No past transactions change; it places new ones." : $"Would also change {Plural(past, "past transaction")}. Ones you set by hand stay as they are.";
            }
            else
            {
                subtitle = "Only new transactions. Past ones stay as they are.";
            }
            var warnings = existing.Count > 0
                ? new List<string> { $"You already have a rule for “{text}”. Rules run in order, so the older one wins where both match." }
                : [];

            var card = Card(AssistantActionKind.Rule, summary, subtitle, [], 0, warnings, null, null, ["transactions", "budget", "rules"]) with { Facts = facts };
            return new Proposal(AssistantActionKind.Rule, new ActionChange { Rule = rule, ApplyToPast = applyToPast }, new ActionBefore(),
                Hash($"{past}|{existing.Count}"), summary, applyToPast ? past : 0, true, card,
                new { summary, past_transactions_it_changes = applyToPast ? past : 0, applies_to_past = applyToPast, similar_rules_already = existing.Count });
        }

        // ---- budget targets ----

        private async Task<Proposal> TargetAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            var cats = await CategoriesAsync(userId, ct);
            var categoryId = input.Int("category_id") ?? throw new ToolInputException("category_id is required.");
            if (!cats.TryGetValue(categoryId, out var category) || category.IsArchived)
            {
                throw new ToolInputException($"Unknown category id {categoryId}. Use an id from the context.");
            }
            var year = input.Int("year") ?? today.Year;
            var month = input.Int("month") ?? today.Month;
            if (month is < 1 or > 12) throw new ToolInputException("month must be 1 to 12.");
            var key = year * 12 + month;
            var nowKey = today.Year * 12 + today.Month;
            if (key < nowKey - 12 || key > nowKey + 12) throw new ToolInputException("Targets can be set from a year back to a year ahead.");

            var m = await budgets.GetMonthAsync(userId, year, month, today, ct);
            var line = m.Lines.FirstOrDefault(l => l.CategoryId == categoryId);
            var old = line?.Target ?? 0;
            var amount = input.Decimal("amount") is decimal a ? a
                : input.Decimal("change") is decimal d ? old + d
                : throw new ToolInputException("Give amount (the new target) or change (how much to raise or lower it).");
            amount = Math.Round(amount, 2);
            if (amount < 0) throw new ToolInputException($"That would put the target below $0 (it's {Money(old)} now).");
            if (amount > 10_000_000) throw new ToolInputException("Targets can be at most $10,000,000.");
            if (amount == old) throw new ToolInputException($"Nothing to change: {category.Name} is already {Money(old)} for {MonthName(year, month)}.");

            var unassignedAfter = m.Unassigned - (amount - old);
            var summary = $"Set {category.Name} to {Money(amount)} for {MonthName(year, month)}";
            var isThisMonth = key == nowKey;
            var card = Card(AssistantActionKind.Target, summary, line != null && isThisMonth ? $"{Money(line.Spent)} spent so far" : null, [], 0,
                unassignedAfter < 0 ? [$"That’s {Money(-unassignedAfter)} more than you have left to assign."] : [], null,
                new AssistantLink("Open the budget", "/budget"), ["budget"]) with
            {
                Changes =
                [
                    new AssistantActionChange("Target", Money(old), Money(amount)),
                    new AssistantActionChange("Not yet assigned", Money(m.Unassigned), Money(unassignedAfter)),
                ],
            };
            return new Proposal(AssistantActionKind.Target,
                new ActionChange { CategoryId = categoryId, CategoryName = category.Name, Year = year, Month = month, Amount = amount, Rollover = line?.Rollover ?? false },
                new ActionBefore { TargetAmount = old, TargetRollover = line?.Rollover ?? false },
                Hash($"{old}|{line?.Rollover}"), summary, 1, true, card,
                new
                {
                    summary,
                    category = category.Name,
                    month = $"{year:0000}-{month:00}",
                    target_now = old,
                    new_target = amount,
                    not_yet_assigned_now = m.Unassigned,
                    not_yet_assigned_after = unassignedAfter,
                    spent_so_far = isThisMonth ? line?.Spent : null,
                });
        }

        // ---- categories ----

        private async Task<Proposal> CategoryAsync(string userId, ToolInput input, CancellationToken ct)
        {
            var cats = await CategoriesAsync(userId, ct);
            var active = cats.Values.Where(c => !c.IsArchived).ToList();
            FinancialCategory Find(string key)
            {
                var id = input.Int(key) ?? throw new ToolInputException($"{key} is required.");
                return cats.TryGetValue(id, out var c) && !c.IsArchived ? c : throw new ToolInputException($"Unknown category id {id}. Use an id from the context.");
            }
            string NewName()
            {
                var name = Clean(input.Str("name")) ?? throw new ToolInputException("name is required.");
                if (name.Length > 100) throw new ToolInputException("Keep category names under 100 characters.");
                var same = active.FirstOrDefault(c => string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (same != null) throw new ToolInputException($"There's already a category called {same.Name} (id {same.FinancialCategoryId}).");
                return name;
            }

            switch (input.Str("op"))
            {
                case "add":
                {
                    var name = NewName();
                    var group = (input.Str("group") ?? "Flexible") switch
                    {
                        "Fixed" => CategoryGroup.Fixed,
                        "NonMonthly" => CategoryGroup.NonMonthly,
                        _ => CategoryGroup.Flexible
                    };
                    var summary = $"Make a new category: {name}";
                    var card = Card(AssistantActionKind.Category, summary, $"In {GroupWords(group)}", [], 0, [], null, null, ["categories", "transactions", "budget"]);
                    return new Proposal(AssistantActionKind.Category, new ActionChange { Op = "add", Name = name, Group = group }, new ActionBefore(),
                        Hash($"add|{name.ToLowerInvariant()}"), summary, 1, true, card, new { summary, name, group = group.ToString() });
                }
                case "rename":
                {
                    var category = Find("category_id");
                    var name = NewName();
                    var summary = $"Rename {category.Name} to {name}";
                    var card = Card(AssistantActionKind.Category, summary, "Its transactions, rules and targets stay with it.", [], 0, [], null, null, ["categories", "transactions", "budget"]) with
                    {
                        Changes = [new AssistantActionChange("Name", category.Name, name)],
                    };
                    return new Proposal(AssistantActionKind.Category, new ActionChange { Op = "rename", CategoryId = category.FinancialCategoryId, Name = name },
                        new ActionBefore { CategoryName = category.Name }, Hash($"rename|{category.Name}"), summary, 1, true, card,
                        new { summary, category_id = category.FinancialCategoryId, name_now = category.Name, new_name = name });
                }
                case "merge":
                {
                    var from = Find("category_id");
                    var into = Find("into_category_id");
                    if (from.FinancialCategoryId == into.FinancialCategoryId) throw new ToolInputException("Those are the same category.");
                    var fromId = from.FinancialCategoryId;
                    var moving = await Mine(userId).CountAsync(t => t.CategoryId == fromId || t.Splits.Any(s => s.CategoryId == fromId), ct);
                    var rules = await context.CategoryRules.CountAsync(r => r.UserId == userId && r.SetCategoryId == fromId, ct);
                    var targets = await context.BudgetTargets.CountAsync(t => t.CategoryId == fromId && t.BudgetMonth.UserId == userId, ct);
                    var summary = $"Merge {from.Name} into {into.Name}";
                    var card = Card(AssistantActionKind.Category, summary, null, [], 0, [$"{from.Name} will be archived. This can’t be undone."], null,
                        new AssistantLink($"See {into.Name} in Transactions", $"/transactions?month=all&category={into.FinancialCategoryId}"), ["categories", "transactions", "budget", "rules"]) with
                    {
                        Facts =
                        [
                            new AssistantHighlight("Transactions that move", moving.ToString(CultureInfo.InvariantCulture)),
                            new AssistantHighlight("Rules that move", rules.ToString(CultureInfo.InvariantCulture)),
                            new AssistantHighlight("Monthly targets added together", targets.ToString(CultureInfo.InvariantCulture)),
                        ],
                        ConfirmLabel = $"Merge and archive {from.Name}",
                        Destructive = true,
                    };
                    return new Proposal(AssistantActionKind.Category, new ActionChange { Op = "merge", CategoryId = fromId, CategoryName = from.Name, IntoCategoryId = into.FinancialCategoryId, Name = into.Name },
                        new ActionBefore { CategoryName = from.Name }, Hash($"merge|{moving}|{rules}|{targets}"), summary, moving, false, card,
                        new { summary, transactions_that_move = moving, rules_that_move = rules, monthly_targets_added_together = targets, can_be_undone = false });
                }
                default:
                    throw new ToolInputException("op must be add, rename or merge.");
            }
        }

        // ---- shared ----

        private static AssistantActionModel Card(AssistantActionKind kind, string title, string? subtitle, List<AssistantActionRow> rows, int total,
            List<string> warnings, AssistantActionRuleOffer? rule, AssistantLink? link, List<string> refresh) =>
            new(Guid.Empty, kind.ToString(), "proposed", title, subtitle, rows, Math.Max(0, total - rows.Count), [], [], warnings, rule,
                "Confirm", false, false, true, DateTimeOffset.MinValue, null, null, link, refresh);

        public static string Hash(IEnumerable<RowBefore> rows) =>
            Hash(string.Join(";", rows.OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.CategoryId}:{r.CategorySource}:{r.Treatment}:{r.TreatmentSource}")));

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        private static Treatment? ParseTreatment(string? value) => value == null ? null
            : Enum.TryParse<Treatment>(value, true, out var t) ? t
            : throw new ToolInputException("treatment must be Spending, Refund, Income, Transfer, CardPayment or Hidden.");

        public static string TreatmentName(Treatment t) => t switch
        {
            Treatment.CardPayment => "Card payment",
            _ => t.ToString()
        };

        private static string TreatmentWords(Treatment t) => t switch
        {
            Treatment.Spending => "spending",
            Treatment.Refund => "refunds",
            Treatment.Income => "income",
            Treatment.Transfer => "transfers",
            Treatment.CardPayment => "card payments",
            _ => "hidden"
        };

        private static string GroupWords(CategoryGroup g) => g switch
        {
            CategoryGroup.Fixed => "Fixed (bills)",
            CategoryGroup.NonMonthly => "Non-monthly",
            _ => "Flexible (day to day)"
        };

        /// <summary>"24 Starbucks transactions", "1 Starbucks transaction"; "3 of these"</summary>
        private static string Plural(int n, string noun) =>
            noun.StartsWith("of ", StringComparison.Ordinal) ? $"{n} {noun}" : $"{n} {noun.TrimStart()}{(n == 1 ? "" : "s")}";

        public static string Money(decimal amount)
        {
            var abs = Math.Abs(amount);
            var text = abs.ToString(abs == Math.Floor(abs) ? "$#,##0" : "$#,##0.00", Us);
            return amount < 0 ? "-" + text : text;
        }

        private static string Day(DateOnly d, bool withYear) => d.ToString(withYear ? "MMM d, yyyy" : "MMM d", Us);

        private static string MonthName(int year, int month) => new DateOnly(year, month, 1).ToString("MMMM yyyy", Us);
    }
}
