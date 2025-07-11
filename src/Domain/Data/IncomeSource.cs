using Elysian.Domain.Constants;
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
    public class IncomeSource : AuditableEntity
    {
        public int IncomeSourceId { get; set; }
        public int InstitutionAccessItemId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal AmountDue { get; set; }
        public int? DayOfMonthDue { get; set; }
        public IncomeSourceType IncomeSourceType { get; set; }
        public List<ExpectedPaymentMemo> ExpectedPaymentMemos { get; set; }
        public InstitutionAccessItem InstitutionAccessItem { get; set; }
        public ICollection<IncomePayment> IncomePayments { get; set; }

        public class Configuration : IEntityTypeConfiguration<IncomeSource>
        {
            public void Configure(EntityTypeBuilder<IncomeSource> builder)
            {
                builder.HasKey(b => b.IncomeSourceId);

                builder.Property(b => b.Name).IsRequired();
                builder.Property(b => b.AmountDue).HasColumnType("decimal(18,2)");
                builder.Property(b => b.IncomeSourceType).HasConversion<string>().IsRequired();

                builder.HasOne(b => b.InstitutionAccessItem)
                    .WithMany()
                    .HasForeignKey(b => b.InstitutionAccessItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.OwnsMany(e => e.ExpectedPaymentMemos, b =>
                {
                    b.ToJson();
                });

                builder.ToTable("IncomeSource").IsMultiTenant();
            }
        }
    }
    public class ExpectedPaymentMemo
    {
        public string Memo { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
