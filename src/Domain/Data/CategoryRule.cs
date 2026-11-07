using Elysian.Domain.Constants;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>"Always do this for …": sets a category and/or treatment on matching transactions.</summary>
    public class CategoryRule : AuditableEntity
    {
        public int CategoryRuleId { get; set; }
        public string UserId { get; set; }
        public RuleField Field { get; set; }
        public RuleMatch Match { get; set; }
        /// <summary>Compared case-insensitively; merchants are compared in their normalized form</summary>
        public string Value { get; set; }
        public RuleDirection Direction { get; set; }
        public int? SetCategoryId { get; set; }
        public Treatment? SetTreatment { get; set; }
        /// <summary>Lower runs first</summary>
        public int Priority { get; set; }

        public FinancialCategory? SetCategory { get; set; }

        public class Configuration : AuditableEntityConfiguration<CategoryRule>
        {
            public override void Configure(EntityTypeBuilder<CategoryRule> builder)
            {
                base.Configure(builder);

                builder.HasKey(r => r.CategoryRuleId);
                builder.Property(r => r.UserId).IsRequired().HasMaxLength(200);
                builder.Property(r => r.Field).HasConversion<string>().HasMaxLength(20);
                builder.Property(r => r.Match).HasConversion<string>().HasMaxLength(10);
                builder.Property(r => r.Value).IsRequired().HasMaxLength(200);
                builder.Property(r => r.Direction).HasConversion<string>().HasMaxLength(10);
                builder.Property(r => r.SetTreatment).HasConversion<string>().HasMaxLength(16);

                // Deleting a category leaves its rules, without a category
                builder.HasOne(r => r.SetCategory)
                    .WithMany()
                    .HasForeignKey(r => r.SetCategoryId)
                    .OnDelete(DeleteBehavior.SetNull);

                builder.HasIndex(r => r.UserId);
                builder.ToTable("CategoryRule").IsMultiTenant();
            }
        }
    }

    /// <summary>One line of a split transaction; the lines add up to the transaction's amount.</summary>
    public class TransactionSplit
    {
        public long TransactionSplitId { get; set; }
        public long BankTransactionId { get; set; }
        public int? CategoryId { get; set; }
        public decimal Amount { get; set; }
        public string? Notes { get; set; }

        public BankTransaction BankTransaction { get; set; }
        public FinancialCategory? Category { get; set; }

        public class Configuration : IEntityTypeConfiguration<TransactionSplit>
        {
            public void Configure(EntityTypeBuilder<TransactionSplit> builder)
            {
                builder.HasKey(s => s.TransactionSplitId);
                builder.Property(s => s.Amount).HasColumnType("decimal(18,2)");
                builder.Property(s => s.Notes).HasMaxLength(500);
                builder.HasOne(s => s.BankTransaction)
                    .WithMany(t => t.Splits)
                    .HasForeignKey(s => s.BankTransactionId)
                    .OnDelete(DeleteBehavior.Cascade);
                // SQL Server refuses two cascade paths to one table; categories are archived, not deleted, in the app
                builder.HasOne(s => s.Category)
                    .WithMany()
                    .HasForeignKey(s => s.CategoryId)
                    .OnDelete(DeleteBehavior.NoAction);
                builder.ToTable("TransactionSplit").IsMultiTenant();
            }
        }
    }
}
