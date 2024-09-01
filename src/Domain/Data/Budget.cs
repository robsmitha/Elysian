using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Finbuckle.MultiTenant;
using Elysian.Domain.Seedwork;

namespace Elysian.Domain.Data
{
    public class Budget : AuditableEntity
    {
        public int BudgetId { get; set; }
        public string Name { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string UserId { get; set; }

        public class Configuration : AuditableEntityConfiguration<Budget>
        {
            public override void Configure(EntityTypeBuilder<Budget> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.BudgetId);
                builder.Property(e => e.Name).IsRequired();
                builder.Property(e => e.StartDate).IsRequired();
                builder.Property(e => e.EndDate).IsRequired();
                builder.Property(e => e.UserId).IsRequired();

                builder.ToTable("Budget").IsMultiTenant();
            }
        }
    }
}
