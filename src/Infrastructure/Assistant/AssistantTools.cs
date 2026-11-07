using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elysian.Application.Features.Assistant.Models;
using Elysian.Application.Features.Financial.Budgeting;
using Elysian.Application.Features.Financial.Categories;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Assistant
{
    /// <summary>What one tool call produced: the JSON for the model, a label for "How I got this", and a chart if it has a series</summary>
    public record ToolOutcome(string Json, bool IsError, string Step, AssistantChart? Chart);

    /// <summary>A tool's input didn't make sense (a bad date, an id that isn't the person's). Goes back to the model as an error.</summary>
    public class ToolInputException(string message) : Exception(message);

    /// <summary>
    /// The assistant's read-only tools (docs/assistant-plan.md §4). Every query starts from the signed-in person's own
    /// accounts on the tenant-filtered context; the model never supplies a user id. Totals, averages and comparisons are
    /// worked out here (or in <see cref="SpendingMath"/>) so the model never does arithmetic.
    /// </summary>
    public class AssistantTools(ElysianContext context, IBudgetMonthService budgets, IGoalService goals, IDebtService debts)
    {
        public const int MaxRows = 25;
        public const int MaxGroups = 15;
        public const int MaxSpanDays = 3 * 366;
        /// <summary>Keeps one result from filling the conversation</summary>
        public const int MaxJson = 14_000;

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        // ---- definitions ----

        private const string Filters = """
            "from": { "type": "string", "description": "First day, YYYY-MM-DD. Default: the first of the month 11 months ago." },
            "to": { "type": "string", "description": "Last day, YYYY-MM-DD. Default: today." },
            "merchant": { "type": "string", "description": "Merchant or description text, e.g. \"starbucks\". Matches the merchant name and the bank's description." },
            "category_ids": { "type": "array", "items": { "type": "integer" }, "description": "Only these categories (ids from the context). 0 means no category." },
            "account_ids": { "type": "array", "items": { "type": "integer" }, "description": "Only these accounts (ids from list_accounts_and_categories)." },
            "include": { "type": "string", "enum": ["counted", "income", "transfers", "card_payments", "hidden", "all"], "description": "counted (default) is what budgets count: spending and refunds on spending accounts." }
            """;

        private static ToolDefinition Def(string name, string description, string properties, string required = "") =>
            new(name, description, JsonDocument.Parse($$"""
                { "type": "object", "properties": { {{properties}} }, "required": [{{required}}], "additionalProperties": false }
                """).RootElement.Clone());

        public static readonly IReadOnlyList<ToolDefinition> Definitions =
        [
            Def("list_accounts_and_categories",
                "The person's linked accounts (with ids, type, budget role and balance) and their active categories (ids, groups). The context already lists names; use this for ids, balances or a refresh.",
                ""),
            Def("find_transactions",
                "Individual transactions matching filters, newest first by default, at most 25 rows. Also returns how many matched in total, total_out (charges), total_in (money in), net, and the merchant names that matched. Use for 'when did I last…', 'show me…', or a total for one merchant.",
                Filters + "," + """
                "min_amount": { "type": "number", "description": "Only amounts at least this large (either direction)." },
                "max_amount": { "type": "number", "description": "Only amounts at most this large (either direction)." },
                "sort": { "type": "string", "enum": ["newest", "oldest", "largest"] },
                "limit": { "type": "integer", "description": "Rows to return, 1 to 25. Default 10." }
                """),
            Def("summarize_spending",
                "Totals grouped by merchant, category, category_group, month, week, account or weekday over a date range, with count, average per transaction and share of the whole. The top groups (default 10, at most 15) plus the rest. Month grouping returns a series that can be charted.",
                Filters + "," + """
                "group_by": { "type": "string", "enum": ["merchant", "category", "category_group", "month", "week", "account", "weekday"] },
                "top_n": { "type": "integer", "description": "Groups to list, 1 to 15. Default 10." }
                """, "\"group_by\""),
            Def("spending_trend",
                "Monthly totals for one category, one merchant, or all counted spending: the last N full months (default 6, 2 to 24) plus this month so far. Returns mean, median, min, max, typical swing %, a steadiness label (steady, some_variation, uneven), trend % per month and direction, each month's % from the average, and this month vs the same day last month. Use for 'is it steady', 'is it going up', 'how does this month compare'. Returns a chartable series.",
                """
                "category_id": { "type": "integer", "description": "One category id from the context." },
                "merchant": { "type": "string", "description": "Merchant text instead of a category." },
                "months": { "type": "integer", "description": "Full months to look back, 2 to 24. Default 6." },
                "include": { "type": "string", "enum": ["counted", "income"], "description": "counted (default), or income to trend money coming in." }
                """),
            Def("compare_periods",
                "Two date ranges side by side: each total and count, the change (second minus first) and % change, and with group_by the groups that moved most. Use for 'more than last month', 'vs last year', 'what changed'.",
                """
                "first_from": { "type": "string", "description": "YYYY-MM-DD" },
                "first_to": { "type": "string", "description": "YYYY-MM-DD" },
                "second_from": { "type": "string", "description": "YYYY-MM-DD" },
                "second_to": { "type": "string", "description": "YYYY-MM-DD" },
                "group_by": { "type": "string", "enum": ["none", "category", "merchant"] },
                "merchant": { "type": "string" },
                "category_ids": { "type": "array", "items": { "type": "integer" } },
                "include": { "type": "string", "enum": ["counted", "income", "transfers", "card_payments", "all"] }
                """, "\"first_from\", \"first_to\", \"second_from\", \"second_to\""),
            Def("get_budget_month",
                "A month's budget: income (expected, received), committed (debt and goals), assigned, spent, safe to spend and per day, and each category's target, available, spent, left, pace status and projected overspend. Default: this month.",
                """
                "year": { "type": "integer" },
                "month": { "type": "integer", "description": "1 to 12" }
                """),
            Def("get_recurring",
                "Recurring charges found from history (bills and subscriptions): cadence, usual amount, price change, last and next date, category, yearly cost, plus monthly and yearly totals.",
                ""),
            Def("get_income",
                "Paychecks: the income streams with their next payday, this month's expected and received income, money in by month for the last N months (default 3, 1 to 12) and the latest deposits.",
                """
                "months": { "type": "integer", "description": "1 to 12. Default 3." }
                """),
            Def("get_goals",
                "Savings goals: target, saved, due date, monthly amount needed, months left, status (OnTrack, Ahead, Behind, Funded, Overdue) and how far ahead or behind.",
                ""),
            Def("get_debt_plan",
                "Debts and the payoff plan: each debt's balance, APR, minimum and payoff month under the chosen strategy, plus totals, the debt-free month and total interest, and how the strategies compare.",
                ""),
            Def("find_unusual",
                "Charges worth a second look in a date range (default this month): well above that merchant's usual amount, the same merchant and amount twice within 3 days, and merchants not seen in the year before.",
                """
                "from": { "type": "string", "description": "YYYY-MM-DD. Default: the first of this month." },
                "to": { "type": "string", "description": "YYYY-MM-DD. Default: today." }
                """),
        ];

        // ---- running a call ----

        public async Task<ToolOutcome> RunAsync(string userId, DateOnly today, ModelToolCall call, CancellationToken cancellationToken)
        {
            try
            {
                var input = new ToolInput(call.Input);
                var (result, step, chart) = call.Name switch
                {
                    "list_accounts_and_categories" => await ListAsync(userId, cancellationToken),
                    "find_transactions" => await FindAsync(userId, today, input, cancellationToken),
                    "summarize_spending" => await SummarizeAsync(userId, today, input, cancellationToken),
                    "spending_trend" => await TrendAsync(userId, today, input, cancellationToken),
                    "compare_periods" => await CompareAsync(userId, today, input, cancellationToken),
                    "get_budget_month" => await BudgetMonthAsync(userId, today, input, cancellationToken),
                    "get_recurring" => await RecurringAsync(userId, today, cancellationToken),
                    "get_income" => await IncomeAsync(userId, today, input, cancellationToken),
                    "get_goals" => await GoalsAsync(userId, today, cancellationToken),
                    "get_debt_plan" => await DebtAsync(userId, today, cancellationToken),
                    "find_unusual" => await UnusualAsync(userId, today, input, cancellationToken),
                    _ => throw new ToolInputException($"There's no tool called {call.Name}.")
                };
                var json = JsonSerializer.Serialize(result, Json);
                if (json.Length > MaxJson)
                {
                    json = JsonSerializer.Serialize(new { error = "That result was too large. Narrow the dates or filters, or group the results." }, Json);
                    return new ToolOutcome(json, true, step, null);
                }
                return new ToolOutcome(json, false, step, chart);
            }
            catch (ToolInputException ex)
            {
                return new ToolOutcome(JsonSerializer.Serialize(new { error = ex.Message }, Json), true, "A lookup that needed different inputs", null);
            }
        }

        // ---- data ----

        private IQueryable<BankTransaction> Mine(string userId) =>
            context.BankTransactions.Where(t => t.RemovedAt == null
                && t.FinancialAccount.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId));

        private IQueryable<FinancialAccount> MyAccounts(string userId) =>
            context.FinancialAccounts.Where(a => a.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId));

        private record Row(long Id, DateOnly Date, decimal Amount, bool Pending, string Merchant, string Description, string MerchantKey,
            int? CategoryId, int AccountId, string Account, Treatment Treatment, List<SplitPart> Splits);

        private record SplitPart(int? CategoryId, decimal Amount);

        /// <summary>One line per transaction, or one per split part: what category totals add up</summary>
        private record Line(Row Row, int? CategoryId, decimal Amount);

        private record Filter(DateOnly From, DateOnly To, string? Merchant, HashSet<int>? CategoryIds, HashSet<int>? AccountIds, string Include);

        private async Task<Filter> ReadFilterAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct, string? fromKey = "from", string? toKey = "to")
        {
            var to = input.Date(toKey ?? "to") ?? today;
            var from = input.Date(fromKey ?? "from") ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
            CheckRange(from, to);
            var include = input.Str("include") ?? "counted";
            if (include is not ("counted" or "income" or "transfers" or "card_payments" or "hidden" or "all"))
            {
                throw new ToolInputException($"include must be counted, income, transfers, card_payments, hidden or all, not {include}.");
            }
            return new Filter(from, to, Clean(input.Str("merchant")),
                await CheckCategoriesAsync(userId, input.IntList("category_ids"), ct),
                await CheckAccountsAsync(userId, input.IntList("account_ids"), ct),
                include);
        }

        private static void CheckRange(DateOnly from, DateOnly to)
        {
            if (to < from) throw new ToolInputException("to is before from.");
            if (to.DayNumber - from.DayNumber > MaxSpanDays) throw new ToolInputException("Use a range of three years or less.");
        }

        private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, 80)];

        private async Task<HashSet<int>?> CheckCategoriesAsync(string userId, List<int>? ids, CancellationToken ct)
        {
            if (ids == null || ids.Count == 0) return null;
            var mine = await context.FinancialCategories.Where(c => c.UserId == userId).Select(c => c.FinancialCategoryId).ToListAsync(ct);
            var unknown = ids.Where(id => id != 0 && !mine.Contains(id)).ToList();
            if (unknown.Count > 0) throw new ToolInputException($"Unknown category id {string.Join(", ", unknown)}. Use ids from the context.");
            return [.. ids];
        }

        private async Task<HashSet<int>?> CheckAccountsAsync(string userId, List<int>? ids, CancellationToken ct)
        {
            if (ids == null || ids.Count == 0) return null;
            var mine = await MyAccounts(userId).Select(a => a.FinancialAccountId).ToListAsync(ct);
            var unknown = ids.Where(id => !mine.Contains(id)).ToList();
            if (unknown.Count > 0) throw new ToolInputException($"Unknown account id {string.Join(", ", unknown)}. Use list_accounts_and_categories.");
            return [.. ids];
        }

        private async Task<List<Row>> RowsAsync(string userId, Filter f, CancellationToken ct, decimal? min = null, decimal? max = null)
        {
            var q = Mine(userId).Where(t => t.BudgetDate >= f.From && t.BudgetDate <= f.To);
            q = f.Include switch
            {
                "counted" => q.Where(t => t.FinancialAccount.BudgetRole == AccountBudgetRole.Spending
                    && (t.Treatment == Treatment.Spending || t.Treatment == Treatment.Refund)),
                "income" => q.Where(t => t.Treatment == Treatment.Income),
                "transfers" => q.Where(t => t.Treatment == Treatment.Transfer),
                "card_payments" => q.Where(t => t.Treatment == Treatment.CardPayment),
                "hidden" => q.Where(t => t.Treatment == Treatment.Hidden),
                _ => q
            };
            if (f.AccountIds != null) q = q.Where(t => f.AccountIds.Contains(t.FinancialAccountId));
            if (f.Merchant != null)
            {
                var pattern = $"%{Escape(f.Merchant)}%";
                var keyPattern = $"%{Escape(CategoryCatalog.MerchantKey(f.Merchant))}%";
                q = q.Where(t => EF.Functions.Like(t.Name, pattern)
                    || (t.MerchantName != null && EF.Functions.Like(t.MerchantName, pattern))
                    || EF.Functions.Like(t.MerchantKey, keyPattern));
            }
            if (min is decimal lo) q = q.Where(t => t.Amount >= lo || t.Amount <= -lo);
            if (max is decimal hi) q = q.Where(t => t.Amount <= hi && t.Amount >= -hi);

            var rows = await q.Select(t => new
            {
                t.BankTransactionId,
                t.BudgetDate,
                t.Amount,
                t.Pending,
                Merchant = t.MerchantName ?? t.Name,
                t.Name,
                t.MerchantKey,
                t.CategoryId,
                t.FinancialAccountId,
                Account = t.FinancialAccount.Mask != null ? t.FinancialAccount.Name + " ••" + t.FinancialAccount.Mask : t.FinancialAccount.Name,
                t.Treatment,
                Splits = t.Splits.Select(s => new { s.CategoryId, s.Amount }).ToList()
            }).ToListAsync(ct);

            var list = rows.Select(r => new Row(r.BankTransactionId, r.BudgetDate, r.Amount, r.Pending, r.Merchant, r.Name, r.MerchantKey,
                r.CategoryId, r.FinancialAccountId, r.Account, r.Treatment, r.Splits.Select(s => new SplitPart(s.CategoryId, s.Amount)).ToList())).ToList();

            if (f.CategoryIds != null)
            {
                list = list.Where(r => r.Splits.Count > 0
                    ? r.Splits.Any(s => f.CategoryIds.Contains(s.CategoryId ?? 0))
                    : f.CategoryIds.Contains(r.CategoryId ?? 0)).ToList();
            }
            return list;
        }

        private static string Escape(string text) => text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

        /// <summary>Split transactions become one line per part; with a category filter, only the matching parts</summary>
        private static List<Line> Lines(IEnumerable<Row> rows, Filter f)
        {
            var lines = new List<Line>();
            foreach (var r in rows)
            {
                if (r.Splits.Count > 0) lines.AddRange(r.Splits.Select(s => new Line(r, s.CategoryId, s.Amount)));
                else lines.Add(new Line(r, r.CategoryId, r.Amount));
            }
            return f.CategoryIds == null ? lines : lines.Where(l => f.CategoryIds.Contains(l.CategoryId ?? 0)).ToList();
        }

        /// <summary>Money in shows as a positive amount when that's what was asked for</summary>
        private static decimal Value(decimal amount, string include) => include == "income" ? -amount : amount;

        private async Task<Dictionary<int, FinancialCategory>> CategoriesAsync(string userId, CancellationToken ct) =>
            await context.FinancialCategories.Where(c => c.UserId == userId).ToDictionaryAsync(c => c.FinancialCategoryId, ct);

        private static string CategoryName(int? id, Dictionary<int, FinancialCategory> cats) =>
            id is int i && cats.TryGetValue(i, out var c) ? c.Name : "No category";

        // ---- links (the only links an answer may use) ----

        /// <summary>
        /// The Transactions page showing exactly what a tool counted: the same dates (one month, or a from–to range), and
        /// for counted totals its Spending view, so the page's count matches the answer's.
        /// </summary>
        private static string TransactionsLink(DateOnly from, DateOnly to, int? categoryId = null, string? q = null, string include = "counted")
        {
            var wholeMonth = from.Day == 1 && to == from.AddMonths(1).AddDays(-1);
            var link = wholeMonth
                ? $"/transactions?month={from:yyyy-MM}"
                : $"/transactions?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
            if (categoryId is int c) link += $"&category={c}";
            if (q != null) link += $"&q={Uri.EscapeDataString(q)}";
            if (include == "counted") link += "&view=spending";
            return link;
        }

        private static DateOnly EndOfMonth(DateOnly first) => first.AddMonths(1).AddDays(-1);

        private static int? OneCategory(Filter f) => f.CategoryIds is { Count: 1 } ? f.CategoryIds.First() : null;

        private static string Dates(DateOnly from, DateOnly to) =>
            from.Year == to.Year
                ? $"{from.ToString("MMM d", CultureInfo.InvariantCulture)} to {to.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"
                : $"{from.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)} to {to.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";

        private static string Describe(Filter f, Dictionary<int, FinancialCategory> cats)
        {
            var parts = new List<string>();
            if (f.Merchant != null) parts.Add($"“{f.Merchant}”");
            if (f.CategoryIds != null) parts.Add(string.Join(", ", f.CategoryIds.Select(id => CategoryName(id == 0 ? null : id, cats))));
            parts.Add(Dates(f.From, f.To));
            if (f.Include != "counted") parts.Add(f.Include.Replace('_', ' '));
            return string.Join(", ", parts);
        }

        // ---- tools ----

        private async Task<(object, string, AssistantChart?)> ListAsync(string userId, CancellationToken ct)
        {
            var accounts = await MyAccounts(userId).OrderBy(a => a.Name)
                .Select(a => new { id = a.FinancialAccountId, a.Name, a.Mask, a.Type, a.Subtype, role = a.BudgetRole.ToString(), balance = a.CurrentBalance, available = a.AvailableBalance })
                .ToListAsync(ct);
            var categories = await context.FinancialCategories.Where(c => c.UserId == userId && !c.IsArchived)
                .OrderBy(c => c.Group).ThenBy(c => c.SortOrder)
                .Select(c => new { id = c.FinancialCategoryId, c.Name, group = c.Group.ToString() })
                .ToListAsync(ct);
            return (new { accounts, categories }, "Your accounts and categories", null);
        }

        private async Task<(object, string, AssistantChart?)> FindAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            var f = await ReadFilterAsync(userId, today, input, ct);
            var rows = await RowsAsync(userId, f, ct, input.Decimal("min_amount"), input.Decimal("max_amount"));
            var cats = await CategoriesAsync(userId, ct);
            var limit = Math.Clamp(input.Int("limit") ?? 10, 1, MaxRows);
            IEnumerable<Row> sorted = (input.Str("sort") ?? "newest") switch
            {
                "oldest" => rows.OrderBy(r => r.Date).ThenBy(r => r.Id),
                "largest" => rows.OrderByDescending(r => Math.Abs(r.Amount)).ThenByDescending(r => r.Date),
                _ => rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
            };
            var result = new
            {
                from = f.From,
                to = f.To,
                include = f.Include,
                match_count = rows.Count,
                total_out = SpendingMath.Round(rows.Where(r => r.Amount > 0).Sum(r => r.Amount)),
                total_in = SpendingMath.Round(-rows.Where(r => r.Amount < 0).Sum(r => r.Amount)),
                net = SpendingMath.Round(rows.Sum(r => r.Amount)),
                first_date = rows.Count > 0 ? rows.Min(r => r.Date) : (DateOnly?)null,
                last_date = rows.Count > 0 ? rows.Max(r => r.Date) : (DateOnly?)null,
                merchants_matched = rows.GroupBy(r => r.Merchant).OrderByDescending(g => g.Count()).Take(10).Select(g => g.Key).ToList(),
                rows = sorted.Take(limit).Select(r => new
                {
                    date = r.Date,
                    merchant = r.Merchant,
                    description = r.Description == r.Merchant ? null : r.Description,
                    amount = r.Amount,
                    direction = r.Amount < 0 ? "in" : "out",
                    category = r.Splits.Count > 0 ? "Split: " + string.Join(", ", r.Splits.Select(s => CategoryName(s.CategoryId, cats))) : CategoryName(r.CategoryId, cats),
                    account = r.Account,
                    treatment = r.Treatment.ToString(),
                    pending = r.Pending ? true : (bool?)null,
                }).ToList(),
                rows_shown = Math.Min(limit, rows.Count),
                link = TransactionsLink(f.From, f.To, OneCategory(f), f.Merchant, f.Include),
            };
            return (result, $"Searched {Describe(f, cats)}", null);
        }

        private static readonly string[] Weekdays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

        private async Task<(object, string, AssistantChart?)> SummarizeAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            var f = await ReadFilterAsync(userId, today, input, ct);
            var groupBy = input.Str("group_by") ?? throw new ToolInputException("group_by is required.");
            var top = Math.Clamp(input.Int("top_n") ?? 10, 1, MaxGroups);
            var cats = await CategoriesAsync(userId, ct);
            var lines = Lines(await RowsAsync(userId, f, ct), f);
            var total = lines.Sum(l => Value(l.Amount, f.Include));

            // key, display name, sort order, link
            Func<Line, (string Key, string Name, string Sort, string? Link)> keyOf = groupBy switch
            {
                "merchant" => l => (l.Row.MerchantKey, l.Row.Merchant, "", TransactionsLink(f.From, f.To, OneCategory(f), l.Row.Merchant, f.Include)),
                "category" => l => ((l.CategoryId ?? 0).ToString(CultureInfo.InvariantCulture), CategoryName(l.CategoryId, cats), "",
                    TransactionsLink(f.From, f.To, l.CategoryId ?? 0, f.Merchant, f.Include)),
                "category_group" => l => (GroupName(l.CategoryId, cats), GroupName(l.CategoryId, cats), "", null),
                "month" => l => ($"{l.Row.Date:yyyy-MM}", l.Row.Date.ToString("MMMM yyyy", CultureInfo.InvariantCulture), $"{l.Row.Date:yyyy-MM}",
                    TransactionsLink(new DateOnly(l.Row.Date.Year, l.Row.Date.Month, 1), EndOfMonth(new DateOnly(l.Row.Date.Year, l.Row.Date.Month, 1)), OneCategory(f), f.Merchant, f.Include)),
                "week" => l => { var w = l.Row.Date.AddDays(-(((int)l.Row.Date.DayOfWeek + 6) % 7)); return ($"{w:yyyy-MM-dd}", $"Week of {w.ToString("MMM d", CultureInfo.InvariantCulture)}", $"{w:yyyy-MM-dd}", null); },
                "account" => l => (l.Row.AccountId.ToString(CultureInfo.InvariantCulture), l.Row.Account, "", null),
                "weekday" => l => { var d = ((int)l.Row.Date.DayOfWeek + 6) % 7; return (Weekdays[d], Weekdays[d], d.ToString(CultureInfo.InvariantCulture), null); },
                _ => throw new ToolInputException("group_by must be merchant, category, category_group, month, week, account or weekday.")
            };

            var groups = lines.GroupBy(l => keyOf(l).Key)
                .Select(g =>
                {
                    var first = keyOf(g.First());
                    var sum = g.Sum(l => Value(l.Amount, f.Include));
                    var count = g.Select(l => l.Row.Id).Distinct().Count();
                    return new
                    {
                        first.Name,
                        first.Sort,
                        first.Link,
                        Total = SpendingMath.Round(sum),
                        Count = count,
                        Average = count == 0 ? 0 : SpendingMath.Round(sum / count),
                        Share = total > 0 ? Math.Round(sum / total * 100, 1) : (decimal?)null,
                    };
                }).ToList();

            var ordered = groupBy is "month" or "week" or "weekday"
                ? groups.OrderBy(g => g.Sort).ToList()
                : groups.OrderByDescending(g => g.Total).ToList();

            // Months with nothing still show, so a chart of months isn't missing bars
            if (groupBy == "month")
            {
                var byKey = groups.ToDictionary(g => g.Sort);
                var listed = new List<object>();
                var labels = new List<string>();
                var totals = new List<decimal>();
                for (var m = new DateOnly(f.From.Year, f.From.Month, 1); m <= f.To; m = m.AddMonths(1))
                {
                    var name = m.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
                    var link = TransactionsLink(m, EndOfMonth(m), OneCategory(f), f.Merchant, f.Include);
                    if (byKey.TryGetValue($"{m:yyyy-MM}", out var g))
                    {
                        listed.Add(new { name, total = g.Total, count = g.Count, average = g.Average, share_pct = g.Share, link });
                        totals.Add(g.Total);
                    }
                    else
                    {
                        listed.Add(new { name, total = 0m, count = 0, average = 0m, share_pct = (decimal?)0, link });
                        totals.Add(0);
                    }
                    labels.Add(m.ToString("MMM", CultureInfo.InvariantCulture));
                }
                var partial = f.To.Year == today.Year && f.To.Month == today.Month;
                var chart = new AssistantChart("bar", "By month", labels, totals, partial, null);
                return (new { from = f.From, to = f.To, include = f.Include, group_by = groupBy, total = SpendingMath.Round(total), months = listed, series = true },
                    $"Monthly totals for {Describe(f, cats)}", chart);
            }

            var shown = ordered.Take(top).ToList();
            var rest = ordered.Skip(top).ToList();
            var result = new
            {
                from = f.From,
                to = f.To,
                include = f.Include,
                group_by = groupBy,
                total = SpendingMath.Round(total),
                transactions = lines.Select(l => l.Row.Id).Distinct().Count(),
                groups = shown.Select(g => new { name = g.Name, total = g.Total, count = g.Count, average = g.Average, share_pct = g.Share, link = g.Link }).ToList(),
                rest = rest.Count == 0 ? null : new { groups = rest.Count, total = SpendingMath.Round(rest.Sum(g => g.Total)), count = rest.Sum(g => g.Count) },
            };
            return (result, $"Totals by {groupBy.Replace('_', ' ')} for {Describe(f, cats)}", null);
        }

        private static string GroupName(int? categoryId, Dictionary<int, FinancialCategory> cats) =>
            categoryId is int i && cats.TryGetValue(i, out var c) ? c.Group.ToString() : "No category";

        private async Task<(object, string, AssistantChart?)> TrendAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            var months = Math.Clamp(input.Int("months") ?? 6, 2, 24);
            var include = input.Str("include") ?? "counted";
            if (include is not ("counted" or "income")) throw new ToolInputException("include must be counted or income.");
            var categoryId = input.Int("category_id");
            var merchant = Clean(input.Str("merchant"));
            var thisMonth = new DateOnly(today.Year, today.Month, 1);
            var f = new Filter(thisMonth.AddMonths(-months), today, merchant,
                categoryId is int c ? await CheckCategoriesAsync(userId, [c], ct) : null, null, include);
            var cats = await CategoriesAsync(userId, ct);
            var lines = Lines(await RowsAsync(userId, f, ct), f);

            var byMonth = lines.GroupBy(l => new DateOnly(l.Row.Date.Year, l.Row.Date.Month, 1))
                .ToDictionary(g => g.Key, g => g.Sum(l => Value(l.Amount, include)));
            var full = Enumerable.Range(0, months).Select(i => thisMonth.AddMonths(i - months)).ToList();
            var values = full.Select(m => SpendingMath.Round(byMonth.GetValueOrDefault(m))).ToList();
            var stats = SpendingMath.Trend(values);
            var soFar = SpendingMath.Round(byMonth.GetValueOrDefault(thisMonth));
            var lastMonth = thisMonth.AddMonths(-1);
            var sameDayLast = SpendingMath.Round(lines.Where(l => l.Row.Date >= lastMonth && l.Row.Date < thisMonth && l.Row.Date.Day <= today.Day)
                .Sum(l => Value(l.Amount, include)));

            var subject = categoryId is int id ? CategoryName(id, cats) : merchant != null ? $"“{merchant}”" : include == "income" ? "Money in" : "All counted spending";
            var result = new
            {
                subject,
                include,
                months = full.Select((m, i) => new
                {
                    month = $"{m:yyyy-MM}",
                    label = m.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                    total = values[i],
                    vs_average_pct = stats.Mean > 0 ? Math.Round((values[i] - stats.Mean) / stats.Mean * 100, 1) : (decimal?)null,
                    link = TransactionsLink(m, EndOfMonth(m), categoryId, merchant, include),
                }).ToList(),
                full_months = new
                {
                    count = stats.Months,
                    mean = stats.Mean,
                    median = stats.Median,
                    min = stats.Min,
                    max = stats.Max,
                    standard_deviation = stats.StandardDeviation,
                    typical_swing_pct = stats.TypicalSwingPct,
                    steadiness = stats.Steadiness,
                    trend_pct_per_month = stats.TrendPctPerMonth,
                    direction = stats.Direction,
                },
                this_month = new
                {
                    month = $"{thisMonth:yyyy-MM}",
                    so_far = soFar,
                    through_day = today.Day,
                    days_in_month = DateTime.DaysInMonth(today.Year, today.Month),
                    same_day_last_month = sameDayLast,
                    change_vs_same_day_last_month_pct = SpendingMath.ChangePct(sameDayLast, soFar),
                    link = TransactionsLink(thisMonth, today, categoryId, merchant, include),
                },
                series = true,
            };
            var chart = new AssistantChart("bar", $"{subject.Trim('“', '”')} by month",
                [.. full.Select(m => m.ToString("MMM", CultureInfo.InvariantCulture)), thisMonth.ToString("MMM", CultureInfo.InvariantCulture)],
                [.. values, soFar], true, stats.Months > 0 ? stats.Mean : null);
            return (result, $"Monthly totals for {subject.Trim('“', '”')}, {Dates(full[0], today)}", chart);
        }

        private async Task<(object, string, AssistantChart?)> CompareAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            DateOnly Need(string key) => input.Date(key) ?? throw new ToolInputException($"{key} is required (YYYY-MM-DD).");
            var a = await ReadFilterAsync(userId, today, input, ct, "first_from", "first_to");
            a = a with { From = Need("first_from"), To = Need("first_to") };
            var b = a with { From = Need("second_from"), To = Need("second_to") };
            CheckRange(b.From, b.To);
            var groupBy = input.Str("group_by") ?? "none";
            var cats = await CategoriesAsync(userId, ct);
            var la = Lines(await RowsAsync(userId, a, ct), a);
            var lb = Lines(await RowsAsync(userId, b, ct), b);
            var ta = la.Sum(l => Value(l.Amount, a.Include));
            var tb = lb.Sum(l => Value(l.Amount, a.Include));

            Func<Line, string>? key = groupBy switch
            {
                "category" => l => CategoryName(l.CategoryId, cats),
                "merchant" => l => l.Row.Merchant,
                "none" => null,
                _ => throw new ToolInputException("group_by must be none, category or merchant.")
            };
            var movers = key == null ? null : SpendingMath.Movers(
                la.GroupBy(key).ToDictionary(g => g.Key, g => g.Sum(l => Value(l.Amount, a.Include))),
                lb.GroupBy(key).ToDictionary(g => g.Key, g => g.Sum(l => Value(l.Amount, a.Include))), 8)
                .Select(m => new { name = m.Name, first = m.First, second = m.Second, change = m.Change, change_pct = m.ChangePct }).ToList();

            var result = new
            {
                include = a.Include,
                first = new { from = a.From, to = a.To, total = SpendingMath.Round(ta), count = la.Select(l => l.Row.Id).Distinct().Count(), link = TransactionsLink(a.From, a.To, OneCategory(a), a.Merchant, a.Include) },
                second = new { from = b.From, to = b.To, total = SpendingMath.Round(tb), count = lb.Select(l => l.Row.Id).Distinct().Count(), link = TransactionsLink(b.From, b.To, OneCategory(b), b.Merchant, b.Include) },
                change = SpendingMath.Round(tb - ta),
                change_pct = SpendingMath.ChangePct(ta, tb),
                movers,
            };
            return (result, $"Compared {Dates(a.From, a.To)} with {Dates(b.From, b.To)}", null);
        }

        private async Task<(object, string, AssistantChart?)> BudgetMonthAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            var year = input.Int("year") ?? today.Year;
            var month = input.Int("month") ?? today.Month;
            if (month is < 1 or > 12 || year < 2000 || year > today.Year + 1) throw new ToolInputException("Give a real year and a month from 1 to 12.");
            var m = await budgets.GetMonthAsync(userId, year, month, today, ct);
            var result = new
            {
                year = m.Year,
                month = m.Month,
                is_current = m.IsCurrent,
                day = m.DayOfMonth,
                days_in_month = m.DaysInMonth,
                days_left = m.DaysLeft,
                has_budget = m.HasBudget,
                income = new { total = m.Income.Total, received = m.Income.Received, expected = m.Income.Expected, typed_in = m.Income.Override },
                committed_debt = m.CommittedDebt,
                committed_goals = m.CommittedGoals,
                committed = m.Committed,
                assigned = m.Assigned,
                not_yet_assigned = m.Unassigned,
                spent = m.Spent,
                pending = m.PendingSpent,
                left_in_budget = m.LeftInBudget,
                safe_to_spend = m.SafeToSpend,
                safe_per_day = m.SafePerDay,
                lines = m.Lines.Select(l => new
                {
                    category_id = l.CategoryId,
                    name = l.Name,
                    group = l.Group.ToString(),
                    target = l.Target,
                    carried = l.Carry == 0 ? (decimal?)null : l.Carry,
                    available = l.Available,
                    spent = l.Spent,
                    left = l.Left,
                    status = l.Status.ToString(),
                    even_pace_by_today = l.ExpectedByNow,
                    projected_over = l.ProjectedOver,
                    link = TransactionsLink(new DateOnly(m.Year, m.Month, 1), EndOfMonth(new DateOnly(m.Year, m.Month, 1)), l.CategoryId),
                }).ToList(),
                uncategorized = new { count = m.Uncategorized.Count, amount = m.Uncategorized.Amount },
                not_counted = new { card_payments = m.NotCounted.CardPaymentsTotal, transfers = m.NotCounted.TransfersTotal, refunds = m.NotCounted.RefundsTotal },
                alerts = m.Alerts.Select(a => a.Message).ToList(),
                link = $"/budget?month={m.Year:D4}-{m.Month:D2}",
            };
            return (result, $"The budget for {new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture)}", null);
        }

        private async Task<(object, string, AssistantChart?)> RecurringAsync(string userId, DateOnly today, CancellationToken ct)
        {
            var list = await budgets.GetRecurringAsync(userId, today, ct);
            var items = list.Select(r => new
            {
                name = r.Name,
                cadence = r.Cadence.ToString(),
                amount = r.Amount,
                varies = r.IsVariable ? true : (bool?)null,
                price_change = r.PriceChange,
                previous_amount = r.PriceChange is decimal p ? r.Amount - p : (decimal?)null,
                last_date = r.LastDate,
                next_date = r.NextDate,
                category = r.CategoryName,
                counted = r.Counted,
                times_seen = r.Occurrences,
                yearly_cost = SpendingMath.Round(r.Amount * SpendingMath.PerYear(r.Cadence.ToString())),
                link = TransactionsLink(today.AddYears(-1), today, null, r.Name, "all"),
            }).ToList();
            var yearly = items.Sum(i => i.yearly_cost);
            return (new
            {
                count = items.Count,
                yearly_total = SpendingMath.Round(yearly),
                monthly_equivalent = SpendingMath.Round(yearly / 12),
                price_changes = items.Count(i => i.price_change != null),
                items,
                link = "/budget",
            }, "Your recurring charges", null);
        }

        private async Task<(object, string, AssistantChart?)> IncomeAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            var months = Math.Clamp(input.Int("months") ?? 3, 1, 12);
            var streams = await budgets.GetIncomeStreamsAsync(userId, ct);
            var month = await budgets.GetMonthAsync(userId, today.Year, today.Month, today, ct);
            var thisMonth = new DateOnly(today.Year, today.Month, 1);
            var from = thisMonth.AddMonths(-months);
            var deposits = await Mine(userId)
                .Where(t => t.BudgetDate >= from && t.BudgetDate <= today && t.Amount < 0 && t.Treatment == Treatment.Income)
                .Select(t => new { t.BudgetDate, t.Amount, Name = t.MerchantName ?? t.Name })
                .ToListAsync(ct);
            var result = new
            {
                streams = streams.Streams.Where(s => s.IsActive).Select(s => new
                {
                    name = s.Name,
                    frequency = s.Frequency.ToString(),
                    expected_amount = s.ExpectedAmount,
                    next_payday = s.NextPayday,
                }).ToList(),
                this_month = new
                {
                    total = month.Income.Total,
                    received = month.Income.Received,
                    still_expected = month.Income.Expected,
                    typed_in = month.Income.Override,
                    paychecks = month.Income.Paychecks.Select(p => new { payday = p.Payday, received_on = p.ReceivedOn, name = p.Name, amount = p.Amount, status = p.Status.ToString() }).ToList(),
                },
                by_month = Enumerable.Range(0, months + 1).Select(i => from.AddMonths(i)).Select(m => new
                {
                    month = $"{m:yyyy-MM}",
                    total = SpendingMath.Round(-deposits.Where(d => d.BudgetDate.Year == m.Year && d.BudgetDate.Month == m.Month).Sum(d => d.Amount)),
                    partial = m == thisMonth ? true : (bool?)null,
                }).ToList(),
                latest = deposits.OrderByDescending(d => d.BudgetDate).Take(5).Select(d => new { date = d.BudgetDate, name = d.Name, amount = -d.Amount }).ToList(),
                link = TransactionsLink(from, today, null, null, "income"),
            };
            return (result, "Your paychecks and money in", null);
        }

        private async Task<(object, string, AssistantChart?)> GoalsAsync(string userId, DateOnly today, CancellationToken ct)
        {
            var g = await goals.GetGoalsAsync(userId, today, ct);
            return (new
            {
                needed_this_month = g.NeededThisMonth,
                total_saved = g.TotalSaved,
                on_track = g.OnTrack,
                goals = g.Goals.Where(x => !x.IsArchived).Select(x => new
                {
                    name = x.Name,
                    target = x.TargetAmount,
                    saved = x.Saved,
                    due = x.DueDate,
                    cycle = x.Cycle.ToString(),
                    monthly_needed = x.RequiredMonthly,
                    months_left = x.MonthsLeft,
                    added_this_month = x.ContributedThisMonth,
                    even_pace_by_today = x.ExpectedByNow,
                    ahead_by = x.Difference,
                    status = x.Status.ToString(),
                    kept_in = x.LinkedAccountName,
                }).ToList(),
                link = "/goals",
            }, "Your savings goals", null);
        }

        private async Task<(object, string, AssistantChart?)> DebtAsync(string userId, DateOnly today, CancellationToken ct)
        {
            var p = await debts.GetPlanAsync(userId, 0, today, ct);
            return (new
            {
                total_balance = p.TotalBalance,
                total_minimums = p.TotalMinimums,
                monthly_amount = p.MonthlyAmount,
                strategy = p.Selected.Summary.Strategy.ToString(),
                months_to_debt_free = p.Selected.Summary.Months,
                debt_free_month = p.Selected.Summary.DebtFreeMonth,
                total_interest = p.Selected.Summary.TotalInterest,
                never_pays_off = p.Selected.Summary.NeverPaysOff ? true : (bool?)null,
                debts = p.Debts.Where(d => d.IsActive).Select(d => new
                {
                    name = d.Name,
                    detail = d.Detail,
                    balance = d.Balance,
                    apr = d.Apr,
                    minimum = d.MinimumPayment,
                    due_day = d.DueDay,
                    paid_off_month = d.PaidOffMonth,
                    balance_may_be_old = d.IsStale ? true : (bool?)null,
                }).ToList(),
                strategies = p.Strategies.Select(s => new { strategy = s.Strategy.ToString(), months = s.Months, debt_free_month = s.DebtFreeMonth, total_interest = s.TotalInterest }).ToList(),
                warnings = p.Selected.Warnings,
                link = "/debt",
            }, "Your debt payoff plan", null);
        }

        private async Task<(object, string, AssistantChart?)> UnusualAsync(string userId, DateOnly today, ToolInput input, CancellationToken ct)
        {
            var to = input.Date("to") ?? today;
            var from = input.Date("from") ?? new DateOnly(today.Year, today.Month, 1);
            CheckRange(from, to);
            var window = new Filter(from, to, null, null, null, "counted");
            var history = window with { From = from.AddYears(-1), To = from.AddDays(-1) };
            static SpendRow Spend(Row r) => new(r.Id, r.Date, r.MerchantKey, r.Merchant, r.Amount);
            var found = SpendingMath.FindUnusual(
                (await RowsAsync(userId, history, ct)).Select(Spend).ToList(),
                (await RowsAsync(userId, window, ct)).Select(Spend).ToList());

            return (new
            {
                from,
                to,
                larger_than_usual = found.Large.Take(10).Select(l => new { date = l.Row.Date, merchant = l.Row.Merchant, amount = l.Row.Amount, usual_amount = l.UsualAmount, times_before = l.PriorCharges, link = TransactionsLink(from, to, null, l.Row.Merchant) }).ToList(),
                possible_double_charges = found.Duplicates.Take(10).Select(d => new { merchant = d.First.Merchant, amount = d.First.Amount, first_date = d.First.Date, second_date = d.Second.Date, link = TransactionsLink(from, to, null, d.First.Merchant) }).ToList(),
                new_merchants = found.NewMerchants.Take(10).Select(r => new { date = r.Date, merchant = r.Merchant, amount = r.Amount, link = TransactionsLink(from, to, null, r.Merchant) }).ToList(),
                link = TransactionsLink(from, to),
            }, $"Looked for unusual charges, {Dates(from, to)}", null);
        }
    }

    /// <summary>Reads a tool call's input forgivingly (numbers as strings, missing fields) and explains anything it can't use</summary>
    public class ToolInput(JsonElement input)
    {
        private JsonElement? Get(string name) =>
            input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

        public string? Str(string name) => Get(name) is { } v ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;

        public int? Int(string name) => Get(name) is { } v
            ? v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i
            : int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var j) ? j
            : throw new ToolInputException($"{name} must be a whole number.")
            : null;

        public decimal? Decimal(string name) => Get(name) is { } v
            ? v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d
            : decimal.TryParse(v.ToString().TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var e) ? e
            : throw new ToolInputException($"{name} must be a number.")
            : null;

        public DateOnly? Date(string name)
        {
            var s = Str(name);
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d : throw new ToolInputException($"{name} must be a date like 2026-11-07.");
        }

        public List<int>? IntList(string name)
        {
            if (Get(name) is not { } v) return null;
            if (v.ValueKind != JsonValueKind.Array) throw new ToolInputException($"{name} must be a list of ids.");
            return v.EnumerateArray().Select(e => e.TryGetInt32(out var i) ? i : throw new ToolInputException($"{name} must be a list of ids.")).ToList();
        }
    }
}
