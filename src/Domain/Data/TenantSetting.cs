using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// A tenant's own values for one settings section, e.g. "Booking". The JSON holds only the properties the
    /// tenant sets; everything else comes from the host's configuration. Secrets never go here.
    /// </summary>
    public class TenantSetting : AuditableEntity
    {
        public int TenantSettingId { get; set; }

        /// <summary>
        /// The configuration section this overrides, matching the settings class's SectionName
        /// </summary>
        public string Section { get; set; }

        public string Json { get; set; }

        public class Configuration : AuditableEntityConfiguration<TenantSetting>
        {
            public override void Configure(EntityTypeBuilder<TenantSetting> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.TenantSettingId);

                builder.Property(e => e.Section).IsRequired().HasMaxLength(64);
                builder.Property(e => e.Json).IsRequired();

                builder.HasIndex(["Section", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_TenantSetting_Section")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                builder.ToTable("TenantSetting");
            }
        }
    }
}
