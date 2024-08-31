﻿using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Finbuckle.MultiTenant;

namespace Elysian.Domain.Data
{
    public class InstitutionAccessItem
    {
        public int InstitutionAccessItemId { get; set; }
        public string UserId { get; set; }
        public string AccessToken { get; set; }
        public string ItemId { get; set; }
        public string InstitutionId { get; set; }

        public class Configuration : IEntityTypeConfiguration<InstitutionAccessItem>
        {
            public void Configure(EntityTypeBuilder<InstitutionAccessItem> builder)
            {
                builder.IsMultiTenant();

                builder.HasKey(k => k.InstitutionAccessItemId);
                builder.Property(e => e.UserId).IsRequired();
                builder.Property(e => e.AccessToken).IsRequired();
                builder.Property(e => e.ItemId).IsRequired();
                builder.Property(e => e.InstitutionId).IsRequired();

                builder.HasIndex(["InstitutionId", "UserId", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_InstitutionAccessItem_InstitutionId_UserId");

                builder.ToTable("InstitutionAccessItem");
            }
        }
    }
}
