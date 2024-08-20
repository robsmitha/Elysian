using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Data
{
    public class BudgetAccessItem
    {
        public int BudgetAccessItemId { get; set; }
        public int BudgetId { get; set; }
        public int InstitutionAccessItemId { get; set; }

        public Budget Budget { get; set; }
        public InstitutionAccessItem InstitutionAccessItem { get; set; }

        public class Configuration : IEntityTypeConfiguration<BudgetAccessItem>
        {
            public void Configure(EntityTypeBuilder<BudgetAccessItem> builder)
            {
                builder.HasKey(b => b.BudgetAccessItemId);

                builder.Property(b => b.BudgetAccessItemId).IsRequired();
                builder.Property(b => b.BudgetId).IsRequired();
                builder.Property(b => b.InstitutionAccessItemId).IsRequired();

                builder.HasOne(b => b.Budget)
                    .WithMany()
                    .HasForeignKey(b => b.BudgetId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(b => b.InstitutionAccessItem)
                    .WithMany()
                    .HasForeignKey(b => b.InstitutionAccessItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.ToTable("BudgetAccessItem");
            }
        }
    }
}
