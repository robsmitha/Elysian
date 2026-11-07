using Elysian.Domain.Constants;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// A card or loan in the payoff plan (Phase 4). Linked debts take their balance from the bank account on every read
    /// (D4: no Liabilities product); APR, minimum and due day are entered by hand and survive syncs.
    /// </summary>
    public class Debt : AuditableEntity
    {
        public int DebtId { get; set; }
        public string UserId { get; set; }
        /// <summary>The linked card or loan; null for one entered by hand</summary>
        public int? FinancialAccountId { get; set; }
        public DebtSource Source { get; set; }
        public string Name { get; set; }
        /// <summary>Hand-entered balance (manual debts only; linked ones read the account)</summary>
        public decimal Balance { get; set; }
        public DateOnly? BalanceAsOf { get; set; }
        /// <summary>Percent, e.g. 24.49</summary>
        public decimal Apr { get; set; }
        public decimal MinimumPayment { get; set; }
        public int? DueDay { get; set; }
        /// <summary>D9: a promotional rate (e.g. 0% balance transfer) until PromoEndsOn</summary>
        public decimal? PromoApr { get; set; }
        public DateOnly? PromoEndsOn { get; set; }
        /// <summary>Unpaid promo balances are charged interest back to the start</summary>
        public bool IsDeferredInterest { get; set; }
        /// <summary>False until the person has entered the APR and minimum (linked debts start with guesses)</summary>
        public bool DetailsConfirmed { get; set; }
        /// <summary>Left out of the plan (a linked card that's paid in full every month, say)</summary>
        public bool IsActive { get; set; } = true;

        public FinancialAccount? FinancialAccount { get; set; }

        public class Configuration : AuditableEntityConfiguration<Debt>
        {
            public override void Configure(EntityTypeBuilder<Debt> builder)
            {
                base.Configure(builder);
                builder.HasKey(d => d.DebtId);
                builder.Property(d => d.UserId).IsRequired().HasMaxLength(200);
                builder.Property(d => d.Name).IsRequired().HasMaxLength(100);
                builder.Property(d => d.Source).HasConversion<string>().HasMaxLength(16);
                builder.Property(d => d.Balance).HasColumnType("decimal(18,2)");
                builder.Property(d => d.Apr).HasColumnType("decimal(6,3)");
                builder.Property(d => d.PromoApr).HasColumnType("decimal(6,3)");
                builder.Property(d => d.MinimumPayment).HasColumnType("decimal(18,2)");
                builder.HasOne(d => d.FinancialAccount).WithMany().HasForeignKey(d => d.FinancialAccountId).OnDelete(DeleteBehavior.SetNull);
                builder.HasIndex(d => new { d.UserId, d.FinancialAccountId });
                builder.ToTable("Debt").IsMultiTenant();
            }
        }
    }

    /// <summary>One per person: the fixed monthly amount for debts, the order, and the schedule frozen when it was saved.</summary>
    public class DebtPlan : AuditableEntity
    {
        public int DebtPlanId { get; set; }
        public string UserId { get; set; }
        /// <summary>A committed outflow: taken out before safe to spend</summary>
        public decimal MonthlyAmount { get; set; }
        public DebtStrategy Strategy { get; set; }
        /// <summary>Debt ids in payoff order, for Custom</summary>
        public string? CustomOrderJson { get; set; }
        /// <summary>First of the month the plan started</summary>
        public DateOnly StartDate { get; set; }
        /// <summary>Planned balances at the start of each month, frozen when the plan was saved (D8: progress is by balance)</summary>
        public string BaselineJson { get; set; } = "[]";

        public class Configuration : AuditableEntityConfiguration<DebtPlan>
        {
            public override void Configure(EntityTypeBuilder<DebtPlan> builder)
            {
                base.Configure(builder);
                builder.HasKey(p => p.DebtPlanId);
                builder.Property(p => p.UserId).IsRequired().HasMaxLength(200);
                builder.Property(p => p.MonthlyAmount).HasColumnType("decimal(18,2)");
                builder.Property(p => p.Strategy).HasConversion<string>().HasMaxLength(16);
                builder.HasIndex(p => p.UserId).IsUnique();
                builder.ToTable("DebtPlan").IsMultiTenant();
            }
        }
    }
}

namespace Elysian.Domain.Constants
{
    public enum DebtSource
    {
        /// <summary>Balance from the linked account; the rest entered by hand</summary>
        Linked,
        Manual
    }

    public enum DebtStrategy
    {
        /// <summary>Highest APR first: least interest</summary>
        Avalanche,
        /// <summary>Smallest balance first: quicker wins</summary>
        Snowball,
        Custom
    }
}
