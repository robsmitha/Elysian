using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Data
{
    public class TransactionCategory
    {
        public int TransactionCategoryId { get; set; }
        public string TransactionId { get; set; }
        public int FinancialCategoryId { get; set; }
        public int BudgetId { get; set; }

        public FinancialCategory FinancialCategory { get; set; }
        public Budget Budget { get; set; }
        public class TransactionCategoryConfiguration : IEntityTypeConfiguration<TransactionCategory>
        {
            public void Configure(EntityTypeBuilder<TransactionCategory> builder)
            {
                builder.HasKey(tc => tc.TransactionCategoryId);

                builder.Property(tc => tc.TransactionCategoryId).IsRequired();
                builder.Property(tc => tc.TransactionId).IsRequired().HasMaxLength(50);
                builder.Property(tc => tc.FinancialCategoryId).IsRequired();
                builder.Property(tc => tc.BudgetId).IsRequired();

                builder.HasOne(tc => tc.FinancialCategory)
                    .WithMany()
                    .HasForeignKey(tc => tc.FinancialCategoryId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(tc => tc.Budget)
                    .WithMany()
                    .HasForeignKey(tc => tc.BudgetId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.ToTable("TransactionCategory");
            }
        }
    }
}
