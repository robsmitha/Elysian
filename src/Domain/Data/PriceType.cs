using Elysian.Domain.Seedwork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Data
{
    public class PriceType : AuditableEntity
    {
        public int PriceTypeId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsVariableCost { get; set; }

        public class Configuration : AuditableEntityConfiguration<PriceType>
        {
            public override void Configure(EntityTypeBuilder<PriceType> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.PriceTypeId);
                builder.Property(e => e.Name).IsRequired();
                builder.HasIndex(e => e.Name).IsUnique().HasDatabaseName("AK_PriceType_Name");

                builder.ToTable("PriceType");
            }
        }
    }
}
