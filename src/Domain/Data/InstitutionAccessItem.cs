using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Finbuckle.MultiTenant;
using Elysian.Domain.Seedwork;

namespace Elysian.Domain.Data
{
    public class InstitutionAccessItem : AuditableEntity
    {
        public int InstitutionAccessItemId { get; set; }
        public string AccessToken { get; set; }
        public string ItemId { get; set; }
        public string InstitutionId { get; set; }

        public ICollection<InstitutionAccessItemUser> AccessUsers { get; set; }

        public class Configuration : AuditableEntityConfiguration<InstitutionAccessItem>
        {
            public override void Configure(EntityTypeBuilder<InstitutionAccessItem> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.InstitutionAccessItemId);
                builder.Property(e => e.AccessToken).IsRequired();
                builder.Property(e => e.ItemId).IsRequired();
                builder.Property(e => e.InstitutionId).IsRequired();

                builder.ToTable("InstitutionAccessItem");
            }
        }
    }
}
