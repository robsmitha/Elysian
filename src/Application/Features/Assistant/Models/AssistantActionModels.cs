namespace Elysian.Application.Features.Assistant.Models
{
    /// <summary>A transaction a proposal would change: date, merchant, amount, current category → new one</summary>
    public record AssistantActionRow(DateOnly Date, string Merchant, decimal Amount, string From, string To);

    /// <summary>One value before and after, e.g. Target $300 → $400</summary>
    public record AssistantActionChange(string Label, string Before, string After);

    /// <summary>The tick box on a recategorize or treatment card: "Also do this for future Starbucks charges"</summary>
    public record AssistantActionRuleOffer(string Label, bool Ticked);

    /// <summary>
    /// The card under an answer (docs/assistant-actions-plan.md §5). Status is "proposed", "confirmed", "cancelled",
    /// "expired", "undone" or "failed". Everything on it was worked out by the server, never written by the model.
    /// </summary>
    public record AssistantActionModel(
        Guid Id,
        string Kind,
        string Status,
        string Title,
        string? Subtitle,
        List<AssistantActionRow> Rows,
        int MoreRows,
        List<AssistantActionChange> Changes,
        List<AssistantHighlight> Facts,
        /// <summary>Things to know before confirming, e.g. "3 of these you set by hand. They’ll move too."</summary>
        List<string> Warnings,
        AssistantActionRuleOffer? RuleOffer,
        string ConfirmLabel,
        /// <summary>Can't be undone (a merge): the card asks for a second, explicit confirm</summary>
        bool Destructive,
        bool CanUndo,
        /// <summary>False when the person can see budgets but not change them</summary>
        bool CanConfirm,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? UndoUntil,
        /// <summary>What happened, once confirmed or undone</summary>
        string? Result,
        /// <summary>Where to see the change</summary>
        AssistantLink? Link,
        /// <summary>Pages whose data changed: "transactions", "budget", "categories", "rules"</summary>
        List<string> Refresh);

    /// <summary>
    /// What Confirm, Cancel or Undo did. Outcome is "done", "stale" (it changed since it was proposed: the card has the
    /// fresh proposal to confirm again), "cancelled" or "undone".
    /// </summary>
    public record AssistantActionResult(string Outcome, string Message, AssistantActionModel Action);
}
