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
    public class PriceType : IAuditableEntitiy
    {
        public int PriceTypeId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ModifiedByUserId { get; set; }
        public DateTime ModifiedAt { get; set; }
        public bool IsDeleted { get; set; }
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
