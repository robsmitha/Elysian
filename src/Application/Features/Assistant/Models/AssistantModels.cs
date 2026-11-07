namespace Elysian.Application.Features.Assistant.Models
{
    /// <summary>An earlier exchange, sent back as plain text: "user" or "assistant"</summary>
    public record AssistantTurn(string Role, string Text);

    /// <summary>
    /// A question from the drawer. <paramref name="Today"/> is the person's own date (time zone); <paramref name="Page"/>
    /// is where the drawer was opened, e.g. "budget" with "2026-11".
    /// </summary>
    public record AskAssistantRequest(string Question, List<AssistantTurn>? History, DateOnly? Today, string? Page, string? PageMonth);

    public record AssistantHighlight(string Label, string Value);

    public record AssistantLink(string Label, string To);

    /// <summary>Chart data copied from a tool result by the server; the model never writes these numbers</summary>
    public record AssistantChart(string Kind, string Title, List<string> Labels, List<decimal> Values, bool LastIsPartial, decimal? Average);

    /// <summary>
    /// The reply. Status is "answered", "needs_clarification", "no_data", "out_of_scope" or "fallback" (couldn't
    /// answer reliably). Steps are plain labels for each lookup, for "How I got this".
    /// </summary>
    public record AssistantReply(
        string Status,
        string Answer,
        List<AssistantHighlight> Highlights,
        AssistantChart? Chart,
        List<AssistantLink> Links,
        List<string> FollowUps,
        List<string> Steps,
        AssistantUsageModel Usage);

    /// <summary>Questions left today. ResetsOn is the person's next date.</summary>
    public record AssistantUsageModel(bool Enabled, int DailyLimit, int UsedToday, int RemainingToday, DateOnly ResetsOn);
}
