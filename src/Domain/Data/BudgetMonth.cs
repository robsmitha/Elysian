using Elysian.Domain.Constants;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>A person's budget for one calendar month: a target per category (Phase 3; replaces Budget).</summary>
    public class BudgetMonth : AuditableEntity
    {
        public int BudgetMonthId { get; set; }
        /// <summary>The owner's external user id</summary>
        public string UserId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        /// <summary>Income typed by hand for this month; replaces the paycheck schedule when set</summary>
        public decimal? IncomeOverride { get; set; }

        public ICollection<BudgetTarget> Targets { get; set; } = [];

        public class Configuration : AuditableEntityConfiguration<BudgetMonth>
        {
            public override void Configure(EntityTypeBuilder<BudgetMonth> builder)
            {
                base.Configure(builder);
                builder.HasKey(b => b.BudgetMonthId);
                builder.Property(b => b.UserId).IsRequired().HasMaxLength(200);
                builder.Property(b => b.IncomeOverride).HasColumnType("decimal(18,2)");
                builder.HasIndex(b => new { b.UserId, b.Year, b.Month }).IsUnique();
                builder.ToTable("BudgetMonth").IsMultiTenant();
            }
        }
    }

    public class BudgetTarget
    {
        public int BudgetTargetId { get; set; }
        public int BudgetMonthId { get; set; }
        public int CategoryId { get; set; }
        public decimal Amount { get; set; }
        /// <summary>What's left (or overspent) carries into next month's target</summary>
        public bool Rollover { get; set; }

        public BudgetMonth BudgetMonth { get; set; }
        public FinancialCategory Category { get; set; }

        public class Configuration : IEntityTypeConfiguration<BudgetTarget>
        {
            public void Configure(EntityTypeBuilder<BudgetTarget> builder)
            {
                builder.HasKey(t => t.BudgetTargetId);
                builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
                builder.HasOne(t => t.BudgetMonth).WithMany(m => m.Targets).HasForeignKey(t => t.BudgetMonthId).OnDelete(DeleteBehavior.Cascade);
                builder.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Cascade);
                builder.HasIndex(t => new { t.BudgetMonthId, t.CategoryId }).IsUnique();
                builder.ToTable("BudgetTarget").IsMultiTenant();
            }
        }
    }

    /// <summary>A recurring paycheck: who pays, how often, and a known payday to count from.</summary>
    public class IncomeStream : AuditableEntity
    {
        public int IncomeStreamId { get; set; }
        public string UserId { get; set; }
        public string Name { get; set; }
        /// <summary>Matches deposits by their normalized merchant/description</summary>
        public string MerchantKey { get; set; }
        public PayFrequency Frequency { get; set; }
        /// <summary>A real payday; the schedule counts forward and back from it</summary>
        public DateOnly AnchorDate { get; set; }
        /// <summary>A typical take-home paycheck</summary>
        public decimal ExpectedAmount { get; set; }
        public bool IsActive { get; set; } = true;

        public class Configuration : AuditableEntityConfiguration<IncomeStream>
        {
            public override void Configure(EntityTypeBuilder<IncomeStream> builder)
            {
                base.Configure(builder);
                builder.HasKey(s => s.IncomeStreamId);
                builder.Property(s => s.UserId).IsRequired().HasMaxLength(200);
                builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
                builder.Property(s => s.MerchantKey).IsRequired().HasMaxLength(200);
                builder.Property(s => s.Frequency).HasConversion<string>().HasMaxLength(16);
                builder.Property(s => s.ExpectedAmount).HasColumnType("decimal(18,2)");
                builder.HasIndex(s => s.UserId);
                builder.ToTable("IncomeStream").IsMultiTenant();
            }
        }
    }

    /// <summary>D11: alerts always show in the app; email is off until it is turned on here.</summary>
    public class NotificationSettings : AuditableEntity
    {
        public int NotificationSettingsId { get; set; }
        public string UserId { get; set; }
        public bool EmailEnabled { get; set; }
        public bool BankAlerts { get; set; } = true;
        public bool BudgetAlerts { get; set; } = true;
        public bool GoalAlerts { get; set; } = true;

        public class Configuration : AuditableEntityConfiguration<NotificationSettings>
        {
            public override void Configure(EntityTypeBuilder<NotificationSettings> builder)
            {
                base.Configure(builder);
                builder.HasKey(s => s.NotificationSettingsId);
                builder.Property(s => s.UserId).IsRequired().HasMaxLength(200);
                builder.HasIndex(s => s.UserId).IsUnique();
                builder.ToTable("NotificationSettings").IsMultiTenant();
            }
        }
    }
}
