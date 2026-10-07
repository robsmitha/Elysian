using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// Append-only record of user management actions: who did what to whom, and when. The target is copied rather
    /// than referenced so entries outlive a deleted <see cref="ManagedUser"/>. Never holds invitation redeem URLs.
    /// </summary>
    public class UserAuditLog
    {
        public long UserAuditLogId { get; set; }

        /// <summary>
        /// The acting user's external id (the client principal's NameIdentifier)
        /// </summary>
        public string ActorUserId { get; set; }

        public string? ActorName { get; set; }

        /// <summary>
        /// See <see cref="Constants.UserAuditAction"/>
        /// </summary>
        public string Action { get; set; }

        public int? TargetManagedUserId { get; set; }
        public string? TargetEntraObjectId { get; set; }
        public string? TargetEmail { get; set; }

        public string? Details { get; set; }

        public DateTimeOffset OccurredAt { get; set; }

        public class Configuration : IEntityTypeConfiguration<UserAuditLog>
        {
            public void Configure(EntityTypeBuilder<UserAuditLog> builder)
            {
                builder.IsMultiTenant();

                builder.HasKey(k => k.UserAuditLogId);
                builder.Property(e => e.ActorUserId).HasMaxLength(128).IsRequired();
                builder.Property(e => e.ActorName).HasMaxLength(256);
                builder.Property(e => e.Action).HasMaxLength(32).IsRequired();
                builder.Property(e => e.TargetEntraObjectId).HasMaxLength(64);
                builder.Property(e => e.TargetEmail).HasMaxLength(320);
                builder.Property(e => e.Details).HasMaxLength(1000);

                builder.HasIndex(e => e.TargetManagedUserId);

                builder.ToTable("UserAuditLog");
            }
        }
    }
}
