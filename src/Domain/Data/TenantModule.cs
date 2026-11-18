using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    public enum TenantModuleStatus
    {
        Active = 1,
        Trial = 2,
        Suspended = 3
    }

    /// <summary>
    /// A module switched on for a tenant. A platform's included modules get a row when the tenant is created,
    /// add-ons when they're bought. No row means the tenant doesn't have the module.
    /// </summary>
    public class TenantModule : AuditableEntity
    {
        public int TenantModuleId { get; set; }

        /// <summary>
        /// Set by the tenant filter on save. Declared so platform-level code can read modules across tenants.
        /// </summary>
        public string TenantId { get; set; }

        /// <summary>
        /// One of <see cref="Platforms.ModuleNames"/>
        /// </summary>
        public string ModuleKey { get; set; }

        public TenantModuleStatus Status { get; set; }

        /// <summary>
        /// The subscription or invoice this module is billed under, once billing exists
        /// </summary>
        public string? BillingReference { get; set; }

        public class Configuration : AuditableEntityConfiguration<TenantModule>
        {
            public override void Configure(EntityTypeBuilder<TenantModule> builder)
            {
                base.Configure(builder);
                ConfigureColumns(builder);

                builder.IsMultiTenant();
            }

            /// <summary>
            /// Shared with PlatformContext, which maps the same table without the tenant filter
            /// </summary>
            public static void ConfigureColumns(EntityTypeBuilder<TenantModule> builder)
            {
                builder.HasKey(k => k.TenantModuleId);

                builder.Property(e => e.TenantId).IsRequired().HasMaxLength(64);
                builder.Property(e => e.ModuleKey).IsRequired().HasMaxLength(64);
                builder.Property(e => e.BillingReference).HasMaxLength(256);

                builder.HasIndex(["ModuleKey", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_TenantModule_ModuleKey")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                builder.ToTable("TenantModule");
            }
        }
    }
}
