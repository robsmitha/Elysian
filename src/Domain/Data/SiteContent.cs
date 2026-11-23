using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// The copy of a tenant's site content (colours, wording, links) that its owner is editing. One per tenant.
    /// Saving changes this row only; visitors see nothing until it is published.
    /// </summary>
    public class SiteContentDraft : AuditableEntity
    {
        public int SiteContentDraftId { get; set; }

        /// <summary>
        /// The content document, checked against the template and normalized before it was stored
        /// </summary>
        public string Json { get; set; }

        /// <summary>
        /// The template version the content was checked against
        /// </summary>
        public string TemplateKey { get; set; }

        public int TemplateVersion { get; set; }

        public class Configuration : AuditableEntityConfiguration<SiteContentDraft>
        {
            public override void Configure(EntityTypeBuilder<SiteContentDraft> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.SiteContentDraftId);

                builder.Property(e => e.Json).IsRequired();
                builder.Property(e => e.TemplateKey).IsRequired().HasMaxLength(64);

                // One draft per tenant
                builder.HasIndex(["TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_SiteContentDraft_Tenant")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                builder.ToTable("SiteContentDraft");
            }
        }
    }

    /// <summary>
    /// One published version of a tenant's site content. A version is written once and never changed, so the
    /// edge can keep a copy forever and going back to an earlier one is only a matter of pointing at it.
    /// </summary>
    public class SiteContentVersion : AuditableEntity
    {
        public int SiteContentVersionId { get; set; }

        /// <summary>
        /// 1 for a tenant's first publish, then counting up
        /// </summary>
        public int Version { get; set; }

        public string Json { get; set; }

        public string TemplateKey { get; set; }

        public int TemplateVersion { get; set; }

        public class Configuration : AuditableEntityConfiguration<SiteContentVersion>
        {
            public override void Configure(EntityTypeBuilder<SiteContentVersion> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.SiteContentVersionId);

                builder.Property(e => e.Json).IsRequired();
                builder.Property(e => e.TemplateKey).IsRequired().HasMaxLength(64);

                // Two publishes at once can't both take the same number
                builder.HasIndex(["TenantId", nameof(Version)])
                    .IsUnique()
                    .HasDatabaseName("AK_SiteContentVersion_Version");

                builder.ToTable("SiteContentVersion");
            }
        }
    }
}
