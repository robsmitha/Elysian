using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Data
{
    public class FinancialCategory
    {
        public int FinancialCategoryId { get; set; }
        public string Name { get; set; }

        public class Configuration : IEntityTypeConfiguration<FinancialCategory>
        {
            public void Configure(EntityTypeBuilder<FinancialCategory> builder)
            {
                builder.HasKey(k => k.FinancialCategoryId);
                builder.Property(e => e.Name).IsRequired();

                builder.ToTable("FinancialCategory");
            }
        }
    }
}
