using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    public enum AssistantActionKind
    {
        Recategorize,
        SetTreatment,
        Rule,
        Target,
        Category
    }

    public enum AssistantActionStatus
    {
        /// <summary>Shown on a card, waiting for the person's Confirm</summary>
        Proposed,
        Confirmed,
        Cancelled,
        /// <summary>Not confirmed within 15 minutes</summary>
        Expired,
        Undone,
        /// <summary>Applying it failed; everything was rolled back</summary>
        Failed
    }

    /// <summary>
    /// A change the budget assistant proposed (docs/assistant-actions-plan.md §3). Claude can only create these; the change
    /// happens when the person confirms. Holds the exact change and each row's values before, never question or answer text.
    /// </summary>
    public class AssistantAction
    {
        /// <summary>The id the card and the confirm, cancel and undo endpoints use; not guessable</summary>
        public Guid AssistantActionId { get; set; }
        /// <summary>The person's external (sign-in) id</summary>
        public string UserId { get; set; } = "";
        public AssistantActionKind Kind { get; set; }
        public AssistantActionStatus Status { get; set; }
        /// <summary>What was asked for (the tool input and the person's date), so a stale proposal can be worked out again</summary>
        public string RequestJson { get; set; } = "{}";
        /// <summary>The exact change: ids and new values</summary>
        public string ChangeJson { get; set; } = "{}";
        /// <summary>Each touched row's values before, for undo</summary>
        public string? BeforeJson { get; set; }
        /// <summary>What applying it created (a rule or category id), for undo</summary>
        public string? AppliedJson { get; set; }
        /// <summary>What the card shows</summary>
        public string CardJson { get; set; } = "{}";
        /// <summary>A hash of the touched rows' values when proposed; confirming checks nothing moved</summary>
        public string Fingerprint { get; set; } = "";
        /// <summary>The card's title, e.g. "Move 24 Starbucks transactions to Coffee"</summary>
        public string Summary { get; set; } = "";
        public int AffectedCount { get; set; }
        public bool CanUndo { get; set; }
        /// <summary>What happened, e.g. "24 moved to Coffee"</summary>
        public string? ResultText { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public DateTimeOffset? UndoUntil { get; set; }
        public DateTimeOffset? UndoneAt { get; set; }

        public class Configuration : IEntityTypeConfiguration<AssistantAction>
        {
            public void Configure(EntityTypeBuilder<AssistantAction> builder)
            {
                builder.HasKey(a => a.AssistantActionId);
                builder.Property(a => a.AssistantActionId).ValueGeneratedNever();
                builder.Property(a => a.UserId).HasMaxLength(200).IsRequired();
                builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
                builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
                builder.Property(a => a.Fingerprint).HasMaxLength(64);
                builder.Property(a => a.Summary).HasMaxLength(200);
                builder.Property(a => a.ResultText).HasMaxLength(300);
                builder.HasIndex(a => new { a.UserId, a.CreatedAt });
                builder.HasIndex(a => new { a.UserId, a.ConfirmedAt });
                builder.ToTable("AssistantAction").IsMultiTenant();
            }
        }
    }
}
