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
    public class User : AuditableEntity
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public string ExternalUserId { get; set; }
        public string IdentityProvider { get; set; }

        public AccessControl AccessControl { get; set; }

        public class Configuration : AuditableEntityConfiguration<User>
        {
            public override void Configure(EntityTypeBuilder<User> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.UserId);

                builder.Property(e => e.Email).IsRequired();

                builder.HasIndex(e => e.ExternalUserId).IsUnique()
                    .HasDatabaseName("AK_User_ExternalUserId");

                builder.Property(e => e.IdentityProvider).IsRequired();

                builder.OwnsOne(b => b.AccessControl, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                });


                builder.ToTable("User").IsMultiTenant();
            }
        }
    }
    public class AccessControl
    {
        public List<string> Roles { get; set; }
        public List<string> Policies { get; set; }
    }
}
