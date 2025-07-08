using Elysian.Domain.Seedwork;
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
    public class InstitutionAccessItemUser : AuditableEntity
    {
        public int InstitutionAccessItemUserId { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }

        public int InstitutionAccessItemId { get; set; }
        public InstitutionAccessItem InstitutionAccessItem { get; set; }

        public class Configuration : AuditableEntityConfiguration<InstitutionAccessItemUser>
        {
            public override void Configure(EntityTypeBuilder<InstitutionAccessItemUser> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.InstitutionAccessItemUserId);

                builder.HasOne(b => b.User)
                    .WithMany(b => b.InstitutionAccessItemUsers)
                    .HasForeignKey(b => b.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(b => b.InstitutionAccessItem)
                    .WithMany(b => b.AccessUsers)
                    .HasForeignKey(b => b.InstitutionAccessItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.ToTable("InstitutionAccessItemUser");
            }
        }
    }
}
