using Elysian.Domain.Seedwork;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Finbuckle.MultiTenant;

namespace Elysian.Domain.Data
{
    public class IncomePayment : AuditableEntity
    {
        public int IncomePaymentId { get; set; }
        public string TransactionId { get; set; }
        public int IncomeSourceId { get; set; }
        public DateTime PaymentDate { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMemo { get; set; }
        public bool IsManualAdjustment { get; set; }
        public IncomeSource IncomeSource { get; set; }

        public class Configuration : IEntityTypeConfiguration<IncomePayment>
        {
            public void Configure(EntityTypeBuilder<IncomePayment> builder)
            {
                builder.HasKey(b => b.IncomePaymentId);

                builder.Property(b => b.Amount).HasColumnType("decimal(18,2)");

                builder.HasOne(b => b.IncomeSource)
                    .WithMany(b => b.IncomePayments)
                    .HasForeignKey(b => b.IncomeSourceId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.ToTable("IncomePayment").IsMultiTenant();
            }
        }
    }
}
