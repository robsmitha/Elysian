using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// A tenant's description of its own site: its name and address, photo categories, featured spots and page
    /// layouts. The site owns it and replaces it whole when the site changes. The admin app and the placement
    /// publisher read it; a visitor's page load never does.
    /// </summary>
    public class SiteManifest : AuditableEntity
    {
        public int SiteManifestId { get; set; }

        /// <summary>
        /// The manifest document, as the site wrote it
        /// </summary>
        public string Json { get; set; }

        /// <summary>
        /// Where this copy came from, e.g. "seed" or "deploy"
        /// </summary>
        public string Source { get; set; }

        public class Configuration : AuditableEntityConfiguration<SiteManifest>
        {
            public override void Configure(EntityTypeBuilder<SiteManifest> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.SiteManifestId);

                builder.Property(e => e.Json).IsRequired();
                builder.Property(e => e.Source).IsRequired().HasMaxLength(32);

                // One current manifest per tenant
                builder.HasIndex(["TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_SiteManifest_Tenant")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                builder.ToTable("SiteManifest");
            }
        }
    }
}
