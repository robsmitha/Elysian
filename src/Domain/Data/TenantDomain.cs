using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// An address a tenant's public site answers at. Every tenant has at most one primary host: the address the
    /// platform itself uses for the site (links, and where it reads the site's own files from) and the one every
    /// other host redirects to. This table is the record; anything at the edge that routes by host is built
    /// from it and can be rebuilt from it.
    /// </summary>
    public class TenantDomain : AuditableEntity
    {
        public int TenantDomainId { get; set; }

        /// <summary>
        /// Set by the tenant filter on save. Declared so platform-level code can find a host's tenant.
        /// </summary>
        public string TenantId { get; set; }

        /// <summary>
        /// Lowercase, with no scheme, port or path, e.g. "www.example.com". See <see cref="Platforms.TenantHosts"/>.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// One of <see cref="Platforms.TenantDomainKinds"/>: how the host reaches the site
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// One of <see cref="Platforms.TenantDomainStatuses"/>
        /// </summary>
        public string Status { get; set; }

        public bool IsPrimary { get; set; }

        public class Configuration : AuditableEntityConfiguration<TenantDomain>
        {
            public override void Configure(EntityTypeBuilder<TenantDomain> builder)
            {
                base.Configure(builder);
                ConfigureColumns(builder);

                builder.IsMultiTenant();
            }

            /// <summary>
            /// Shared with PlatformContext, which maps the same table without the tenant filter
            /// </summary>
            public static void ConfigureColumns(EntityTypeBuilder<TenantDomain> builder)
            {
                builder.HasKey(k => k.TenantDomainId);

                builder.Property(e => e.TenantId).IsRequired().HasMaxLength(64);
                builder.Property(e => e.Host).IsRequired().HasMaxLength(253);
                builder.Property(e => e.Kind).IsRequired().HasMaxLength(32);
                builder.Property(e => e.Status).IsRequired().HasMaxLength(32);

                // Unique across every tenant: a request arrives with a host and nothing else
                builder.HasIndex(e => e.Host)
                    .IsUnique()
                    .HasDatabaseName("AK_TenantDomain_Host")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                // One primary host per tenant
                builder.HasIndex(["TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_TenantDomain_Primary")
                    .HasFilter($"[{nameof(IsPrimary)}] = 1 AND [{nameof(IsDeleted)}] = 0");

                builder.ToTable("TenantDomain");
            }
        }
    }
}
