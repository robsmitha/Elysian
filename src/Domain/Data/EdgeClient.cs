using Elysian.Domain.Seedwork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// What an edge client may claim to act for
    /// </summary>
    public enum EdgeClientScope
    {
        /// <summary>One tenant: a site built for a single owner</summary>
        Tenant = 1,

        /// <summary>Every tenant on one platform's shared template</summary>
        Platform = 2,

        /// <summary>Any tenant the signed-in user is a member of: the admin app</summary>
        Admin = 3
    }

    /// <summary>
    /// Something at the edge that forwards requests to the host and says which tenant they're for, e.g. the
    /// Worker for one site. Each has its own key, and the host only believes a tenant claim that the client's
    /// scope covers, so a leaked key can't be used to act as a different tenant.
    /// Belongs to the platform, not to a tenant.
    /// </summary>
    public class EdgeClient : AuditableEntity
    {
        public int EdgeClientId { get; set; }

        /// <summary>
        /// Lowercase letters, digits and dashes, e.g. "site-aspensmitha". Sent as the first part of the credential.
        /// </summary>
        public string Name { get; set; }

        public EdgeClientScope Scope { get; set; }

        /// <summary>
        /// The one tenant a <see cref="EdgeClientScope.Tenant"/> client acts for
        /// </summary>
        public string? TenantIdentifier { get; set; }

        /// <summary>
        /// The platform a <see cref="EdgeClientScope.Platform"/> client serves
        /// </summary>
        public string? Platform { get; set; }

        /// <summary>
        /// SHA-256 of the current key. The key itself is shown once when created and never stored.
        /// </summary>
        public string KeyHash { get; set; }

        /// <summary>
        /// The key before the last rotation, still accepted until it's retired so the edge can be updated without downtime
        /// </summary>
        public string? PreviousKeyHash { get; set; }

        public bool IsEnabled { get; set; } = true;

        public class Configuration : AuditableEntityConfiguration<EdgeClient>
        {
            public override void Configure(EntityTypeBuilder<EdgeClient> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.EdgeClientId);

                builder.Property(e => e.Name).IsRequired().HasMaxLength(64);
                builder.Property(e => e.TenantIdentifier).HasMaxLength(450);
                builder.Property(e => e.Platform).HasMaxLength(64);
                builder.Property(e => e.KeyHash).IsRequired().HasMaxLength(64);
                builder.Property(e => e.PreviousKeyHash).HasMaxLength(64);

                builder.HasIndex(e => e.Name)
                    .IsUnique()
                    .HasDatabaseName("AK_EdgeClient_Name")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                builder.ToTable("EdgeClient");
            }
        }
    }
}
