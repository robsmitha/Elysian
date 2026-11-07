using Elysian.Domain.Constants;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// A transaction stored from Plaid's /transactions/sync. Written only by the sync; budgets read these rows instead
    /// of calling Plaid. Amounts keep Plaid's sign: positive is money out of the account.
    /// </summary>
    public class BankTransaction
    {
        public long BankTransactionId { get; set; }
        public int FinancialAccountId { get; set; }
        public string PlaidTransactionId { get; set; }
        /// <summary>On a posted transaction, the id of the pending one it replaced</summary>
        public string? PendingTransactionId { get; set; }
        public decimal Amount { get; set; }
        /// <summary>The posted date</summary>
        public DateOnly Date { get; set; }
        public DateOnly? AuthorizedDate { get; set; }
        /// <summary>When it happened (authorized date, else posted date); the month it counts in</summary>
        public DateOnly BudgetDate { get; set; }
        public string Name { get; set; }
        public string? MerchantName { get; set; }
        public string? MerchantEntityId { get; set; }
        public string? PfcPrimary { get; set; }
        public string? PfcDetailed { get; set; }
        public string? PfcConfidence { get; set; }
        public string? PaymentChannel { get; set; }
        public string? TransactionType { get; set; }
        public string? IsoCurrencyCode { get; set; }
        public bool Pending { get; set; }
        public DateTimeOffset SyncedAt { get; set; }
        /// <summary>Plaid removed it (a pending charge that dropped off or posted under a new id)</summary>
        public DateTimeOffset? RemovedAt { get; set; }

        /// <summary>The merchant (or description) normalized for rules and refunds: "TRADER JOE S #552" → "trader joe s"</summary>
        public string MerchantKey { get; set; } = "";

        // What budgets do with it (docs/budget-plan.md §4.4). Sources say who decided; a person's choice is never overwritten.
        public int? CategoryId { get; set; }
        public ClassificationSource CategorySource { get; set; }
        public Treatment Treatment { get; set; }
        public ClassificationSource TreatmentSource { get; set; }
        public string? Notes { get; set; }
        /// <summary>When the classifier last looked at it; null means "classify me" (new or changed by Plaid)</summary>
        public DateTimeOffset? ClassifiedAt { get; set; }

        public FinancialAccount FinancialAccount { get; set; }
        public FinancialCategory? Category { get; set; }
        public ICollection<TransactionSplit> Splits { get; set; } = [];

        public class Configuration : IEntityTypeConfiguration<BankTransaction>
        {
            public void Configure(EntityTypeBuilder<BankTransaction> builder)
            {
                builder.HasKey(t => t.BankTransactionId);
                builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(200);
                builder.Property(t => t.PendingTransactionId).HasMaxLength(200);
                builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
                builder.Property(t => t.Name).IsRequired().HasMaxLength(500);
                builder.Property(t => t.MerchantName).HasMaxLength(200);
                builder.Property(t => t.MerchantEntityId).HasMaxLength(100);
                builder.Property(t => t.PfcPrimary).HasMaxLength(100);
                builder.Property(t => t.PfcDetailed).HasMaxLength(100);
                builder.Property(t => t.PfcConfidence).HasMaxLength(20);
                builder.Property(t => t.PaymentChannel).HasMaxLength(20);
                builder.Property(t => t.TransactionType).HasMaxLength(20);
                builder.Property(t => t.IsoCurrencyCode).HasMaxLength(10);
                builder.Property(t => t.MerchantKey).IsRequired().HasMaxLength(200);
                builder.Property(t => t.CategorySource).HasConversion<string>().HasMaxLength(8);
                builder.Property(t => t.Treatment).HasConversion<string>().HasMaxLength(16);
                builder.Property(t => t.TreatmentSource).HasConversion<string>().HasMaxLength(8);
                builder.Property(t => t.Notes).HasMaxLength(1000);

                builder.HasOne(t => t.Category)
                    .WithMany()
                    .HasForeignKey(t => t.CategoryId)
                    .OnDelete(DeleteBehavior.SetNull);

                builder.HasOne(t => t.FinancialAccount)
                    .WithMany(a => a.Transactions)
                    .HasForeignKey(t => t.FinancialAccountId)
                    .OnDelete(DeleteBehavior.Cascade);

                // A transaction is stored once, however many syncs see it
                builder.HasIndex(t => t.PlaidTransactionId).IsUnique();
                builder.HasIndex(t => new { t.FinancialAccountId, t.BudgetDate });

                builder.ToTable("BankTransaction").IsMultiTenant();
            }
        }
    }
}
