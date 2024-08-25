using Elysian.Domain.Seedwork;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Data
{
    public class MerchantType : IAuditableEntitiy
    {
        public int MerchantTypeId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ModifiedByUserId { get; set; }
        public DateTime ModifiedAt { get; set; }
        public bool IsDeleted { get; set; }
        public class Configuration : AuditableEntityConfiguration<MerchantType>
        {
            public override void Configure(EntityTypeBuilder<MerchantType> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.MerchantTypeId);
                builder.Property(e => e.Name).IsRequired();
                builder.HasIndex(e => e.Name).IsUnique().HasDatabaseName("AK_MerchantType_Name");

                builder.ToTable("MerchantType");
            }
        }
    }
}
