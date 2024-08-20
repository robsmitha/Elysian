using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elysian.Domain.Data
{
    public class BudgetExcludedTransaction
    {
        public int BudgetExcludedTransactionId { get; set; }
        public string TransactionId { get; set; }
        public int BudgetId { get; set; }

        public Budget Budget { get; set; }

        public class BudgetExcludedTransactionConfiguration : IEntityTypeConfiguration<BudgetExcludedTransaction>
        {
            public void Configure(EntityTypeBuilder<BudgetExcludedTransaction> builder)
            {
                builder.HasKey(bet => bet.BudgetExcludedTransactionId);

                builder.Property(bet => bet.BudgetExcludedTransactionId).IsRequired();
                builder.Property(bet => bet.TransactionId).IsRequired().HasMaxLength(50);
                builder.Property(bet => bet.BudgetId).IsRequired();

                builder.HasOne(bet => bet.Budget)
                    .WithMany()
                    .HasForeignKey(bet => bet.BudgetId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.ToTable("BudgetExcludedTransaction");
            }
        }
    }
}
