﻿using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Data
{
    public class ProductImage : AuditableEntity
    {
        public int ProductImageId { get; set; }
        public Guid StorageId { get; set; }
        public string FileName { get; set; }
        public string AltText { get; set; }
        public long FileSize { get; set; }
        public bool IsStorageDeleted { get; set; }
        public int ProductId { get; set; }
        public  Product Product { get; set; }

        public class Configuration : AuditableEntityConfiguration<ProductImage>
        {
            public override void Configure(EntityTypeBuilder<ProductImage> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.ProductImageId);
                builder.Property(e => e.StorageId).IsRequired();
                builder.HasIndex(["StorageId", "TenantId"]).IsUnique().HasDatabaseName("AK_ProductImage_StorageId");
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
