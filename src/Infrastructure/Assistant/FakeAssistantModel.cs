using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Elysian.Infrastructure.Assistant
{
    /// <summary>
    /// The offline stand-in for Claude (Assistant__Provider = fake, the default): local dev and tests run the whole flow
    /// with no key and no cost. It picks a tool from keywords, then answers with numbers taken from the real tool result,
    /// so the answer checks pass. Every answer starts with "[sample]". Test switches in the question: "#bad" answers with
    /// a number no tool gave (twice, so it falls back), "#refuse" refuses, "#slow" waits until the time runs out.
    /// </summary>
    public partial class FakeAssistantModel : IAssistantModel
    {
        public const string Name = "fake";

        public async Task<ModelResponse> CreateAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(400, cancellationToken);
            var question = request.Messages.OfType<UserText>().Select(m => m.Text).FirstOrDefault(t => t.Contains("Question:")) ?? "";
            var q = question[(question.LastIndexOf("Question:", StringComparison.Ordinal) + 9)..].Trim();

            if (q.Contains("#slow")) await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            if (q.Contains("#refuse")) return new ModelResponse(ModelStop.Refusal, "", [], new ModelUsage(500, 0, 0, 0), Name, null);

            var results = request.Messages.OfType<ToolResults>().LastOrDefault();
            if (results == null)
            {
                var (tool, input) = Pick(q, question);
                var call = new ModelToolCall($"fake_{Guid.NewGuid():N}"[..20], tool, JsonDocument.Parse(input).RootElement.Clone());
                return new ModelResponse(ModelStop.ToolUse, "", [call], new ModelUsage(1200, 80, 0, 0), Name, null);
            }

            var calls = request.Messages.OfType<AssistantResponse>().LastOrDefault()?.Response.ToolCalls ?? [];
            var first = results.Results[0];
            using var doc = JsonDocument.Parse(first.Content);
            var root = doc.RootElement;
            var answer = Answer(q, root, first, calls.FirstOrDefault()?.Name ?? "", q.Contains("#bad"));
            return new ModelResponse(ModelStop.EndTurn, answer, [], new ModelUsage(1800, 160, 0, 0), Name, null);
        }

        /// <summary>The person's category whose name the text names, from the context's "Name (Group, id N)" list</summary>
        private static int? CategoryId(string text, string context)
        {
            var name = text.Trim().TrimEnd('.', '!', '?').Trim().ToLowerInvariant();
            var match = Categories().Matches(context).FirstOrDefault(m => m.Groups["name"].Value.Trim().ToLowerInvariant() == name)
                ?? Categories().Matches(context).FirstOrDefault(m => name.Contains(m.Groups["name"].Value.Trim().ToLowerInvariant()));
            return match != null ? int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture) : null;
        }

        private static string Input(Dictionary<string, object> input) => JsonSerializer.Serialize(input);

        /// <summary>"Move Starbucks to Coffee", "always put Shell in Gas", "set Dining to $400", "make a Coffee category"…</summary>
        private static (string Tool, string Input)? PickChange(string q, string context)
        {
            var text = q.Replace("#done", "").Trim().TrimEnd('.', '!', '?');
            Match m;
            if ((m = Always().Match(text)).Success && CategoryId(m.Groups["c"].Value, context) is int ruleCategory)
            {
                return ("propose_rule", Input(new() { ["match_text"] = m.Groups["m"].Value.Trim(), ["set_category_id"] = ruleCategory }));
            }
            if ((m = Rename().Match(text)).Success && CategoryId(m.Groups["a"].Value, context) is int renamed)
            {
                return ("propose_category", Input(new() { ["op"] = "rename", ["category_id"] = renamed, ["name"] = m.Groups["b"].Value.Trim() }));
            }
            if ((m = Merge().Match(text)).Success && CategoryId(m.Groups["a"].Value, context) is int from && CategoryId(m.Groups["b"].Value, context) is int into)
            {
                return ("propose_category", Input(new() { ["op"] = "merge", ["category_id"] = from, ["into_category_id"] = into }));
            }
            if ((m = NewCategory().Match(text)).Success)
            {
                return ("propose_category", Input(new() { ["op"] = "add", ["name"] = m.Groups["c"].Value.Trim() }));
            }
            if ((m = SetTarget().Match(text)).Success && CategoryId(m.Groups["c"].Value, context) is int targetCategory)
            {
                return ("propose_target", Input(new() { ["category_id"] = targetCategory, ["amount"] = decimal.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) }));
            }
            if ((m = Treat().Match(text)).Success)
            {
                var treatment = m.Groups["t"].Value.ToLowerInvariant() switch { "hide" => "Hidden", "transfer" or "transfers" => "Transfer", _ => "Income" };
                return ("propose_set_treatment", Input(new() { ["merchant"] = m.Groups["m"].Value.Trim(), ["treatment"] = treatment }));
            }
            if ((m = Move().Match(text)).Success)
            {
                var merchant = m.Groups["m"].Value.Trim();
                var future = q.Contains("from now on", StringComparison.OrdinalIgnoreCase) || q.Contains("always", StringComparison.OrdinalIgnoreCase);
                return CategoryId(m.Groups["c"].Value, context) is int to
                    ? ("propose_recategorize", Input(new() { ["merchant"] = merchant, ["to_category_id"] = to, ["include_rule"] = future }))
                    : ("propose_category", Input(new() { ["op"] = "add", ["name"] = m.Groups["c"].Value.Trim() }));
            }
            return null;
        }

        private static (string Tool, string Input) Pick(string q, string context)
        {
            if (PickChange(q, context) is { } change) return change;
            var lower = q.ToLowerInvariant();
            var category = Categories().Matches(context).FirstOrDefault(m => lower.Contains(m.Groups["name"].Value.Trim().ToLowerInvariant()));
            var categoryInput = category != null ? $"\"category_id\": {category.Groups["id"].Value}" : null;

            if (Has(lower, "steady", "trend", "going up", "consistent", "usually")) return ("spending_trend", $"{{{categoryInput ?? ""}}}");
            if (Has(lower, "subscription", "recurring", "bills")) return ("get_recurring", "{}");
            if (Has(lower, "on track", "budget", "left to spend", "safe to spend", "can i still")) return ("get_budget_month", "{}");
            if (Has(lower, "goal")) return ("get_goals", "{}");
            if (Has(lower, "debt", "pay off", "paid off", "payoff")) return ("get_debt_plan", "{}");
            if (Has(lower, "paycheck", "income", "earn", "paid me")) return ("get_income", "{}");
            if (Has(lower, "unusual", "twice", "double", "weird", "strange")) return ("find_unusual", "{}");
            if (Has(lower, "where did", "breakdown", "top ", "most on")) return ("summarize_spending", "{\"group_by\": \"category\"}");
            if (categoryInput != null) return ("summarize_spending", $"{{\"group_by\": \"month\", \"category_ids\": [{category!.Groups["id"].Value}]}}");

            var merchant = Merchant().Match(q);
            return merchant.Success
                ? ("find_transactions", JsonSerializer.Serialize(new Dictionary<string, object> { ["merchant"] = merchant.Groups["m"].Value.Trim() }))
                : ("summarize_spending", "{\"group_by\": \"category\"}");
        }

        private static bool Has(string text, params string[] words) => words.Any(text.Contains);

        private static string Answer(string question, JsonElement root, ToolResult result, string tool, bool bad)
        {
            if (result.IsError)
            {
                return Reply("no_data", "[sample] That lookup didn't work: " + (root.TryGetProperty("error", out var e) ? e.GetString() : "unknown error"), [], "none", "", []);
            }
            if (root.TryGetProperty("action_id", out var actionId))
            {
                // "#done" claims it's already done, to exercise the check
                var said = question.Contains("#done") ? "[sample] Done! I've moved them." : "[sample] Here's what I'd change. Confirm below to apply it.";
                return Reply("answered", said, [], "none", "", [], actionId.GetString()!);
            }

            // The first few money-like numbers at the top of the result, with their names as labels
            var highlights = new List<object>();
            foreach (var p in root.EnumerateObject())
            {
                if (highlights.Count == 3) break;
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var d) && !p.Name.EndsWith("_pct") && p.Name is not ("year" or "month" or "day"))
                {
                    var money = !(p.Name.Contains("count") || p.Name.Contains("months") || p.Name.Contains("transactions") || p.Name == "on_track");
                    highlights.Add(new { label = Label(p.Name), value = money ? d.ToString("C", CultureInfo.GetCultureInfo("en-US")) : d.ToString(CultureInfo.InvariantCulture) });
                }
            }
            var series = root.TryGetProperty("series", out var s) && s.ValueKind == JsonValueKind.True;
            var link = root.TryGetProperty("link", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : null;
            var text = bad
                ? "[sample] You spent $917.00 on that, more than ever."
                : $"[sample] Here's what the {tool.Replace('_', ' ')} lookup found for “{question}”.";
            return Reply("answered", text, highlights, series ? "bar" : "none", series ? result.ToolCallId : "",
                link != null ? [new { label = "See the details", to = link }] : []);
        }

        private static string Label(string name)
        {
            var words = name.Replace('_', ' ');
            return char.ToUpperInvariant(words[0]) + words[1..];
        }

        private static string Reply(string status, string answer, List<object> highlights, string chartKind, string chartId, List<object> links, string actionId = "") =>
            JsonSerializer.Serialize(new
            {
                status,
                answer,
                highlights,
                chart_kind = chartKind,
                chart_tool_call_id = chartId,
                links,
                follow_ups = new[] { "How does that compare to last month?", "Where did most of it go?" },
                action_id = actionId,
            });

        [GeneratedRegex(@"^(?:always|from now on,?)\s+(?:put|move|send)\s+(?<m>.+?)\s+(?:in|into|to|under)\s+(?<c>.+)$", RegexOptions.IgnoreCase)]
        private static partial Regex Always();

        [GeneratedRegex(@"^(?:please\s+)?(?:move|put|recategori[sz]e|change)\s+(?:all\s+)?(?:of\s+)?(?:my\s+)?(?:the\s+)?(?<m>.+?)\s+(?:to|in|into|under)\s+(?<c>.+?)(?:\s+from now on)?$", RegexOptions.IgnoreCase)]
        private static partial Regex Move();

        [GeneratedRegex(@"^set\s+(?:my\s+)?(?<c>.+?)\s+(?:target\s+|budget\s+)?to\s+\$?(?<n>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
        private static partial Regex SetTarget();

        [GeneratedRegex(@"^(?:make|create|add)\s+(?:a\s+|an\s+)?(?:new\s+)?(?<c>.+?)\s+category$", RegexOptions.IgnoreCase)]
        private static partial Regex NewCategory();

        [GeneratedRegex(@"^rename\s+(?<a>.+?)\s+to\s+(?<b>.+)$", RegexOptions.IgnoreCase)]
        private static partial Regex Rename();

        [GeneratedRegex(@"^merge\s+(?<a>.+?)\s+into\s+(?<b>.+)$", RegexOptions.IgnoreCase)]
        private static partial Regex Merge();

        [GeneratedRegex(@"^(?:(?<t>hide)\s+(?:all\s+)?(?:my\s+)?(?:the\s+)?(?<m>.+)|(?<m>.+?)\s+(?:is|are)\s+(?:a\s+)?(?<t>transfers?|income))$", RegexOptions.IgnoreCase)]
        private static partial Regex Treat();

        [GeneratedRegex(@"(?<name>[^,:\n]+?) \((?:Fixed|Flexible|NonMonthly), id (?<id>\d+)\)")]
        private static partial Regex Categories();

        [GeneratedRegex(@"\b(?:at|on|from|to)\s+(?<m>[A-Za-z0-9'&. ]{2,40}?)(?:\s+(?:total|this|last|in|since|so far)\b|[?.!]|$)", RegexOptions.IgnoreCase)]
        private static partial Regex Merchant();
    }
}
