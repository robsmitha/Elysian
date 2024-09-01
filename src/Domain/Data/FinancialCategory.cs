using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Elysian.Domain.Seedwork;

namespace Elysian.Domain.Data
{
    public class FinancialCategory : AuditableEntity
    {
        public int FinancialCategoryId { get; set; }
        public string Name { get; set; }

        public class Configuration : AuditableEntityConfiguration<FinancialCategory>
        {
            public override void Configure(EntityTypeBuilder<FinancialCategory> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.FinancialCategoryId);
                builder.Property(e => e.Name).IsRequired();

                builder.ToTable("FinancialCategory");
            }
        }
    }
}
