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
    public class Merchant : AuditableEntity
    {
        public int MerchantId { get; set; }
        public string MerchantIdentifier { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string WebsiteUrl { get; set; }
        public bool SelfBoardingApplication { get; set; }
        public bool IsBillable { get; set; }
        public int MerchantTypeId { get; set; }

        public MerchantType MerchantType { get; set; }

        public class Configuration : AuditableEntityConfiguration<Merchant>
        {
            public override void Configure(EntityTypeBuilder<Merchant> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.MerchantId);
                builder.Property(e => e.Name).IsRequired();
                builder.HasIndex(e => e.MerchantIdentifier).IsUnique().HasDatabaseName("AK_Merchant_MerchantIdentifier");

                builder.HasOne(b => b.MerchantType)
                    .WithMany()
                    .HasForeignKey(b => b.MerchantTypeId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.ToTable("Merchant");
            }
        }
    }
}
