using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    public enum AssistantOutcome
    {
        /// <summary>Taken from the limit; not finished yet (or the process stopped)</summary>
        Started,
        Answered,
        /// <summary>The answer failed its checks twice, or ran out of time or steps</summary>
        Fallback,
        Refused,
        Error
    }

    /// <summary>
    /// One question to the budget assistant (docs/assistant-plan.md §7): what the limits count and what it cost.
    /// Deliberately holds no question or answer text.
    /// </summary>
    public class AssistantUsage
    {
        public long AssistantUsageId { get; set; }
        /// <summary>The person's external (sign-in) id, as on their categories</summary>
        public string UserId { get; set; } = "";
        /// <summary>The person's own date when they asked; the daily limit counts by it</summary>
        public DateOnly LocalDate { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public AssistantOutcome Outcome { get; set; }
        public string? Model { get; set; }
        public string? PromptVersion { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CacheReadTokens { get; set; }
        public int ToolCalls { get; set; }
        public int DurationMs { get; set; }

        public class Configuration : IEntityTypeConfiguration<AssistantUsage>
        {
            public void Configure(EntityTypeBuilder<AssistantUsage> builder)
            {
                builder.HasKey(u => u.AssistantUsageId);
                builder.Property(u => u.UserId).HasMaxLength(200).IsRequired();
                builder.Property(u => u.Outcome).HasConversion<string>().HasMaxLength(20);
                builder.Property(u => u.Model).HasMaxLength(100);
                builder.Property(u => u.PromptVersion).HasMaxLength(40);
                builder.HasIndex(u => new { u.UserId, u.StartedAt });
                builder.HasIndex(u => u.LocalDate);
                builder.ToTable("AssistantUsage").IsMultiTenant();
            }
        }
    }
}
