using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// Where a site's contact form messages go: an address its owner entered and proved they can read. Nothing
    /// is ever sent to an address a visitor or an owner merely typed; <see cref="Email"/> is only set by
    /// confirming a code that was sent to it. One per tenant.
    /// </summary>
    public class TenantContactEmail : AuditableEntity
    {
        public int TenantContactEmailId { get; set; }

        /// <summary>
        /// The confirmed address, or null until one has been confirmed
        /// </summary>
        public string? Email { get; set; }

        public DateTimeOffset? VerifiedAt { get; set; }

        /// <summary>
        /// An address waiting for its code to be entered
        /// </summary>
        public string? PendingEmail { get; set; }

        /// <summary>
        /// A hash of the code sent to <see cref="PendingEmail"/>. The code itself is never stored.
        /// </summary>
        public string? CodeHash { get; set; }

        public DateTimeOffset? CodeExpiresAt { get; set; }

        public DateTimeOffset? CodeSentAt { get; set; }

        /// <summary>
        /// Wrong codes entered since the last one was sent; too many and a new code has to be asked for
        /// </summary>
        public int FailedAttempts { get; set; }

        public class Configuration : AuditableEntityConfiguration<TenantContactEmail>
        {
            public override void Configure(EntityTypeBuilder<TenantContactEmail> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.TenantContactEmailId);

                builder.Property(e => e.Email).HasMaxLength(320);
                builder.Property(e => e.PendingEmail).HasMaxLength(320);
                builder.Property(e => e.CodeHash).HasMaxLength(64);

                builder.HasIndex(["TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_TenantContactEmail_Tenant")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                builder.ToTable("TenantContactEmail");
            }
        }
    }

    /// <summary>
    /// How many emails a tenant has sent on one day, so one site can't use up what every site shares
    /// </summary>
    public class TenantEmailCount : AuditableEntity
    {
        public int TenantEmailCountId { get; set; }

        /// <summary>
        /// The day in UTC
        /// </summary>
        public DateOnly Day { get; set; }

        public int Count { get; set; }

        public class Configuration : AuditableEntityConfiguration<TenantEmailCount>
        {
            public override void Configure(EntityTypeBuilder<TenantEmailCount> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.TenantEmailCountId);

                builder.HasIndex(["TenantId", nameof(Day)])
                    .IsUnique()
                    .HasDatabaseName("AK_TenantEmailCount_Day");

                builder.ToTable("TenantEmailCount");
            }
        }
    }
}
