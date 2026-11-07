using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Elysian.Infrastructure.Assistant
{
    /// <summary>
    /// The system prompt (a versioned file, Prompts/budget-assistant-v2.md) and the answer format. Both are the same on
    /// every request so they're cached; everything that changes per request goes in the user turn (<see cref="Context"/>).
    /// </summary>
    public static class AssistantPrompt
    {
        public const string Version = "budget-assistant-v2";

        public static readonly string System = Load($"{Version}.md");

        public static readonly JsonElement AnswerSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["status", "answer", "highlights", "chart_kind", "chart_tool_call_id", "links", "follow_ups", "action_id"],
          "properties": {
            "status": { "type": "string", "enum": ["answered", "needs_clarification", "no_data", "out_of_scope"] },
            "answer": { "type": "string" },
            "highlights": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["label", "value"],
                "properties": { "label": { "type": "string" }, "value": { "type": "string" } }
              }
            },
            "chart_kind": { "type": "string", "enum": ["none", "bar", "line"] },
            "chart_tool_call_id": { "type": "string" },
            "links": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["label", "to"],
                "properties": { "label": { "type": "string" }, "to": { "type": "string" } }
              }
            },
            "follow_ups": { "type": "array", "items": { "type": "string" } },
            "action_id": { "type": "string" }
          }
        }
        """).RootElement.Clone();

        public record ContextAccount(string Name, string? Mask, string Type, string Role);
        public record ContextCategory(int Id, string Name, string Group);

        private static readonly Dictionary<string, string> Pages = new()
        {
            ["dashboard"] = "Dashboard",
            ["budget"] = "Budget",
            ["transactions"] = "Transactions",
            ["goals"] = "Goals",
            ["debt"] = "Debt",
        };

        /// <summary>The short block at the top of the question: today, where the drawer was opened, the person's accounts and categories</summary>
        public static string Context(DateOnly today, string? page, string? pageMonth, IReadOnlyList<ContextAccount> accounts,
            IReadOnlyList<ContextCategory> categories, string question)
        {
            var sb = new StringBuilder();
            sb.Append("Today is ").Append(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
              .Append(" (").Append(today.DayOfWeek).Append(").");
            if (page != null && Pages.TryGetValue(page, out var pageName))
            {
                sb.Append(" Opened from: ").Append(pageName);
                if (pageMonth != null && DateOnly.TryParseExact(pageMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                {
                    sb.Append(", ").Append(m.ToString("MMMM yyyy", CultureInfo.InvariantCulture));
                }
                sb.Append('.');
            }
            sb.AppendLine();
            sb.Append("Accounts: ").AppendLine(accounts.Count == 0 ? "none linked."
                : string.Join(", ", accounts.Select(a => $"{a.Name}{(a.Mask != null ? " ••" + a.Mask : "")} ({a.Type}, {a.Role})")) + ".");
            sb.Append("Categories: ").AppendLine(categories.Count == 0 ? "none."
                : string.Join(", ", categories.Select(c => $"{c.Name} ({c.Group}, id {c.Id})")) + ".");
            sb.AppendLine();
            sb.Append("Question: ").Append(question.Trim());
            return sb.ToString();
        }

        private static string Load(string file)
        {
            var name = $"Elysian.Assistant.Prompts.{file}";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"The prompt {name} isn't embedded");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
