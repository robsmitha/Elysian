using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elysian.Application.Features.Assistant.Models;

namespace Elysian.Application.Features.Assistant
{
    /// <summary>Claude's final answer as it arrives, before the checks (docs/assistant-plan.md §6)</summary>
    public record AnswerDraft(
        string Status,
        string Answer,
        List<AssistantHighlight> Highlights,
        string ChartToolCallId,
        string ChartKind,
        List<AssistantLink> Links,
        List<string> FollowUps);

    public record AnswerCheckResult(AnswerDraft Draft, List<string> UnsupportedNumbers)
    {
        public bool Passed => UnsupportedNumbers.Count == 0;
    }

    /// <summary>
    /// The checks an answer passes before anyone sees it: every amount, percentage and count comes from a tool result
    /// (or the question), links are ones the tools gave, and a chart names a tool call that returned a series.
    /// </summary>
    public static partial class AnswerChecks
    {
        public static readonly string[] Statuses = ["answered", "needs_clarification", "no_data", "out_of_scope"];
        public static readonly string[] ChartKinds = ["none", "bar", "line"];
        public const int MaxHighlights = 3;
        public const int MaxLinks = 2;
        public const int MaxFollowUps = 3;
        public const int MaxAnswer = 1200;

        /// <summary>Whole numbers up to this size, with no $ or %, are allowed without a source: days, months, "3 visits" style counts.</summary>
        public const int FreeSmallNumber = 31;

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        /// <summary>The answer JSON, or null when it isn't the agreed shape</summary>
        public static AnswerDraft? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                var status = Str(root, "status");
                var answer = Str(root, "answer");
                if (!Statuses.Contains(status) || string.IsNullOrWhiteSpace(answer)) return null;

                var highlights = Array(root, "highlights")
                    .Select(h => new AssistantHighlight(Str(h, "label"), Str(h, "value")))
                    .Where(h => h.Label.Length > 0 && h.Value.Length > 0)
                    .Take(MaxHighlights).ToList();
                var links = Array(root, "links")
                    .Select(l => new AssistantLink(Str(l, "label"), Str(l, "to")))
                    .Where(l => l.Label.Length > 0 && l.To.Length > 0)
                    .Take(MaxLinks).ToList();
                var followUps = Array(root, "follow_ups")
                    .Where(f => f.ValueKind == JsonValueKind.String).Select(f => f.GetString()!.Trim())
                    .Where(f => f.Length is > 0 and <= 120)
                    .Take(MaxFollowUps).ToList();
                var kind = Str(root, "chart_kind");

                return new AnswerDraft(status, answer.Length > MaxAnswer ? answer[..MaxAnswer] : answer, highlights,
                    Str(root, "chart_tool_call_id"), ChartKinds.Contains(kind) ? kind : "none", links, followUps);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Checks a draft. Links and charts that aren't backed by a tool result are dropped quietly; numbers that aren't
        /// are returned as problems, so the caller can ask once more or fall back.
        /// </summary>
        public static AnswerCheckResult Check(AnswerDraft draft, IReadOnlyCollection<decimal> supported, string question,
            IReadOnlySet<string> allowedLinks, IReadOnlySet<string> chartToolCallIds)
        {
            var known = new HashSet<decimal>(supported.Select(Math.Abs));
            foreach (var n in Numbers(question)) known.Add(n.Value);

            var problems = new List<string>();
            foreach (var text in new[] { draft.Answer }.Concat(draft.Highlights.Select(h => h.Value)))
            {
                foreach (var n in Numbers(text))
                {
                    if (!IsSupported(n, known) && !problems.Contains(n.Text)) problems.Add(n.Text);
                }
            }

            var links = draft.Links.Where(l => allowedLinks.Contains(l.To) && IsAppRoute(l.To)).ToList();
            var chartOk = draft.ChartKind != "none" && chartToolCallIds.Contains(draft.ChartToolCallId);
            var cleaned = draft with
            {
                Links = links,
                ChartToolCallId = chartOk ? draft.ChartToolCallId : "",
                ChartKind = chartOk ? draft.ChartKind : "none",
            };
            return new AnswerCheckResult(cleaned, problems);
        }

        /// <summary>Only pages of this app, never another site or a script URL</summary>
        public static bool IsAppRoute(string to) => AppRoute().IsMatch(to);

        /// <summary>Every number in a tool result, at any depth</summary>
        public static void CollectNumbers(JsonElement element, ICollection<decimal> into)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number when element.TryGetDecimal(out var d):
                    into.Add(Math.Abs(d));
                    break;
                case JsonValueKind.Object:
                    foreach (var p in element.EnumerateObject()) CollectNumbers(p.Value, into);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) CollectNumbers(item, into);
                    break;
            }
        }

        public record FoundNumber(string Text, decimal Value, bool IsMoney, bool IsPercent);

        /// <summary>Amounts, percentages and counts in a piece of text, leaving out dates and years</summary>
        public static List<FoundNumber> Numbers(string text)
        {
            var withoutDates = Dates().Replace(text ?? "", " ");
            var found = new List<FoundNumber>();
            foreach (Match m in Number().Matches(withoutDates))
            {
                if (!decimal.TryParse(m.Groups["num"].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) continue;
                if (m.Groups["k"].Success) value *= 1000;
                found.Add(new FoundNumber(m.Value.Trim(), value, m.Groups["cur"].Success, m.Groups["pct"].Success));
            }
            return found;
        }

        private static bool IsSupported(FoundNumber n, HashSet<decimal> known)
        {
            if (!n.IsMoney && !n.IsPercent && n.Value == Math.Floor(n.Value) && n.Value <= FreeSmallNumber) return true;
            if (known.Contains(n.Value)) return true;

            foreach (var s in known)
            {
                if (Math.Abs(s - n.Value) < 0.005m) return true;
                // Rounded to the dollar or whole percent ($574.83 → $575), or to one decimal place (7.24% → 7.2%)
                if (n.Value == Math.Floor(n.Value) && Math.Round(s, 0, MidpointRounding.AwayFromZero) == n.Value) return true;
                if (Math.Round(s, 1, MidpointRounding.AwayFromZero) == n.Value) return true;
                // "$1.2k"
                if (n.Value >= 1000 && Math.Abs(s - n.Value) <= 50 && n.Text.EndsWith('k')) return true;
            }
            return false;
        }

        private static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

        private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : [];

        [GeneratedRegex(@"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\.?\s+\d{1,2}(?:st|nd|rd|th)?(?:,?\s+\d{4})?|\b\d{4}-\d{2}(?:-\d{2})?\b|\b\d{1,2}/\d{1,2}(?:/\d{2,4})?\b|\b(?:19|20)\d{2}\b|\b\d{1,2}(?:st|nd|rd|th)\b", RegexOptions.IgnoreCase)]
        private static partial Regex Dates();

        [GeneratedRegex(@"(?<![\w.])(?<cur>\$)?(?<num>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?<k>k\b)?(?<pct>\s?%|\s?percent\b)?", RegexOptions.IgnoreCase)]
        private static partial Regex Number();

        [GeneratedRegex(@"^/(transactions|budget|goals|debt|dashboard)(\?[A-Za-z0-9_=&%.\-]*)?$")]
        private static partial Regex AppRoute();
    }
}
