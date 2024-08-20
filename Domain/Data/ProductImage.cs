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
    public class ProductImage : IAuditableEntitiy
    {
        public int ProductImageId { get; set; }
        public Guid StorageId { get; set; }
        public string FileName { get; set; }
        public string AltText { get; set; }
        public long FileSize { get; set; }
        public bool IsStorageDeleted { get; set; }
        public string CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ModifiedByUserId { get; set; }
        public DateTime ModifiedAt { get; set; }
        public bool IsDeleted { get; set; }
        public int ProductId { get; set; }
        public  Product Product { get; set; }

        public class Configuration : AuditableEntityConfiguration<ProductImage>
        {
            public override void Configure(EntityTypeBuilder<ProductImage> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.ProductImageId);
                builder.Property(e => e.StorageId).IsRequired();
                builder.HasIndex(e => e.StorageId).IsUnique().HasDatabaseName("AK_ProductImage_StorageId");
                builder.Property(e => e.FileName).IsRequired();

                builder.HasOne(b => b.Product)
                    .WithMany()
                    .HasForeignKey(b => b.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.ToTable("ProductImage");
            }
        }
    }
}
