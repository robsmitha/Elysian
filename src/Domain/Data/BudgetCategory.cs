﻿using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations.Schema;
using Finbuckle.MultiTenant;
using Elysian.Domain.Seedwork;

namespace Elysian.Domain.Data
{
    public class BudgetCategory : AuditableEntity
    {
        public int BudgetCategoryId { get; set; }
        public decimal Estimate { get; set; }
        public int BudgetId { get; set; }
        public int FinancialCategoryId { get; set; }

        public Budget Budget { get; set; }
        public FinancialCategory FinancialCategory { get; set; }

        public class BudgetCategoryConfiguration : AuditableEntityConfiguration<BudgetCategory>
        {
            public override void Configure(EntityTypeBuilder<BudgetCategory> builder)
            {
                base.Configure(builder);

                builder.HasKey(bc => bc.BudgetCategoryId);

                builder.Property(bc => bc.BudgetCategoryId).IsRequired();
                builder.Property(bc => bc.Estimate).IsRequired().HasColumnType("decimal(18,2)");
                builder.Property(bc => bc.BudgetId).IsRequired();
                builder.Property(bc => bc.FinancialCategoryId).IsRequired();

                builder.HasOne(bc => bc.Budget)
                    .WithMany()
                    .HasForeignKey(bc => bc.BudgetId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(bc => bc.FinancialCategory)
                    .WithMany()
                    .HasForeignKey(bc => bc.FinancialCategoryId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.ToTable("BudgetCategory").IsMultiTenant();
            }
        }
    }
}
