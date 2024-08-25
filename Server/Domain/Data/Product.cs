using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Elysian.Domain.Seedwork;
using System.ComponentModel.DataAnnotations;
using Finbuckle.MultiTenant;

namespace Elysian.Domain.Data
{
    public class Product : IAuditableEntitiy
    {
        public int ProductId { get; set; }
        public string SerialNumber { get; set; }
        /// <summary>
        /// Name of the item
        /// </summary>
        public string Name { get; set; }
        public string Description { get; set; }
        public string Grade { get; set; }

        /// <summary>
        /// Cost of the item to merchant
        /// </summary>
        public decimal? Cost { get; set; }

        /// <summary>
        /// Price of the item
        /// </summary>
        public decimal? Price { get; set; }

        /// <summary>
        /// True if this item should be counted as revenue, for example gift cards and donations would not
        /// </summary>
        public bool IsRevenue { get; set; }

        /// <summary>
        /// Product code, e.g. UPC or EAN
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// SKU of the item
        /// </summary>
        public string Sku { get; set; }

        /// <summary>
        /// Flag to indicate whether or not to use default tax rates
        /// </summary>
        public bool DefaultTaxRates { get; set; }
        public string LookupCode { get; set; }
        public decimal? Percentage { get; set; }

        public string CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ModifiedByUserId { get; set; }
        public DateTime ModifiedAt { get; set; }
        public bool IsDeleted { get; set; }

        public int MerchantId { get; set; }
        public Merchant Merchant { get; set; }
        public int ProductTypeId { get; set; }
        public ProductType ProductType { get; set; }
        public int PriceTypeId { get; set; }
        public PriceType PriceType { get; set; }
        public int UnitTypeId { get; set; }
        public UnitType UnitType { get; set; }


        public class Configuration : AuditableEntityConfiguration<Product>
        {
            public override void Configure(EntityTypeBuilder<Product> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.ProductId);
                builder.Property(e => e.SerialNumber).IsRequired();
                builder.HasIndex(e => e.SerialNumber)
                    .IsUnique()
                    .HasDatabaseName("AK_Product_SerialNumber")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                builder.Property(e => e.Name).IsRequired();
                builder.Property(e => e.Grade).IsRequired();

                builder.HasOne(b => b.Merchant)
                    .WithMany()
                    .HasForeignKey(b => b.MerchantId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(b => b.ProductType)
                    .WithMany()
                    .HasForeignKey(b => b.ProductTypeId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(b => b.PriceType)
                    .WithMany()
                    .HasForeignKey(b => b.PriceTypeId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(b => b.UnitType)
                    .WithMany()
                    .HasForeignKey(b => b.UnitTypeId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.ToTable("Product").IsMultiTenant();
            }
        }
    }
}
