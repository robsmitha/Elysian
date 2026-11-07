using Elysian.Domain.Constants;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// Money set aside for a known bill or a target (Phase 5). Repeating goals start a new cycle after the due date;
    /// finished cycles are kept in PastCyclesJson.
    /// </summary>
    public class SavingsGoal : AuditableEntity
    {
        public int SavingsGoalId { get; set; }
        public string UserId { get; set; }
        public string Name { get; set; }
        public string? Icon { get; set; }
        public decimal TargetAmount { get; set; }
        public DateOnly DueDate { get; set; }
        /// <summary>When the current cycle began; contributions before it belong to earlier cycles</summary>
        public DateOnly CycleStart { get; set; }
        public GoalCycle Cycle { get; set; }
        public GoalCycleEnd CycleEnd { get; set; }
        /// <summary>D10: Shared = saved is the tracked contributions; Dedicated = saved is the linked account's balance</summary>
        public GoalFundingMode FundingMode { get; set; }
        /// <summary>Where the money is kept (optional for Shared, required for Dedicated)</summary>
        public int? LinkedAccountId { get; set; }
        /// <summary>Already saved when the cycle began (or the surplus rolled over)</summary>
        public decimal StartingBalance { get; set; }
        public bool IsArchived { get; set; }
        /// <summary>Finished cycles: start, due, target and saved</summary>
        public string PastCyclesJson { get; set; } = "[]";

        public FinancialAccount? LinkedAccount { get; set; }
        public ICollection<GoalContribution> Contributions { get; set; } = [];

        public class Configuration : AuditableEntityConfiguration<SavingsGoal>
        {
            public override void Configure(EntityTypeBuilder<SavingsGoal> builder)
            {
                base.Configure(builder);
                builder.HasKey(g => g.SavingsGoalId);
                builder.Property(g => g.UserId).IsRequired().HasMaxLength(200);
                builder.Property(g => g.Name).IsRequired().HasMaxLength(100);
                builder.Property(g => g.Icon).HasMaxLength(64);
                builder.Property(g => g.TargetAmount).HasColumnType("decimal(18,2)");
                builder.Property(g => g.StartingBalance).HasColumnType("decimal(18,2)");
                builder.Property(g => g.Cycle).HasConversion<string>().HasMaxLength(16);
                builder.Property(g => g.CycleEnd).HasConversion<string>().HasMaxLength(16);
                builder.Property(g => g.FundingMode).HasConversion<string>().HasMaxLength(16);
                builder.HasOne(g => g.LinkedAccount).WithMany().HasForeignKey(g => g.LinkedAccountId).OnDelete(DeleteBehavior.SetNull);
                builder.HasIndex(g => g.UserId);
                builder.ToTable("SavingsGoal").IsMultiTenant();
            }
        }
    }

    /// <summary>Money into a goal (or out of it, negative, when the bill is paid)</summary>
    public class GoalContribution
    {
        public int GoalContributionId { get; set; }
        public int SavingsGoalId { get; set; }
        public DateOnly Date { get; set; }
        public decimal Amount { get; set; }
        /// <summary>The transfer it came from; a transfer counts toward one goal once</summary>
        public long? BankTransactionId { get; set; }
        public ContributionSource Source { get; set; }
        public string? Note { get; set; }

        public SavingsGoal SavingsGoal { get; set; }

        public class Configuration : IEntityTypeConfiguration<GoalContribution>
        {
            public void Configure(EntityTypeBuilder<GoalContribution> builder)
            {
                builder.HasKey(c => c.GoalContributionId);
                builder.Property(c => c.Amount).HasColumnType("decimal(18,2)");
                builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(16);
                builder.Property(c => c.Note).HasMaxLength(200);
                builder.HasOne(c => c.SavingsGoal).WithMany(g => g.Contributions).HasForeignKey(c => c.SavingsGoalId).OnDelete(DeleteBehavior.Cascade);
                builder.HasIndex(c => c.BankTransactionId).IsUnique().HasFilter("[BankTransactionId] IS NOT NULL");
                builder.ToTable("GoalContribution").IsMultiTenant();
            }
        }
    }
}

namespace Elysian.Domain.Constants
{
    public enum GoalCycle
    {
        OneTime,
        Annual,
        Semiannual,
        Quarterly
    }

    public enum GoalCycleEnd
    {
        /// <summary>The next cycle starts at $0</summary>
        Reset,
        /// <summary>Anything saved over the target starts the next cycle</summary>
        RollOverSurplus
    }

    public enum GoalFundingMode
    {
        Shared,
        Dedicated
    }

    public enum ContributionSource
    {
        Manual,
        Transfer
    }
}
