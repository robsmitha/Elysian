using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// Someone an admin has given access to through Microsoft Entra ID: a B2B guest we invited, or an existing member
    /// of the tenant. The Entra fields are a cached copy, refreshed by a sync, so lists render without calling Graph per row.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="User"/> because invited people have no SWA user id until they first sign in.
    /// Linked to their <see cref="User"/> by normalized email, at invite time or on first sign-in.
    /// Removed outright on hard delete; the audit log keeps the history.
    /// </remarks>
    public class ManagedUser : AuditableEntity
    {
        public int ManagedUserId { get; set; }

        /// <summary>
        /// Object id of the user in Entra ID
        /// </summary>
        public string EntraObjectId { get; set; }

        /// <summary>
        /// Trimmed and lower case
        /// </summary>
        public string Email { get; set; }

        public string DisplayName { get; set; }

        /// <summary>
        /// Guest or Member, from Graph
        /// </summary>
        public string? UserType { get; set; }

        /// <summary>
        /// See <see cref="Constants.InvitationStatus"/>
        /// </summary>
        public string InvitationStatus { get; set; }

        /// <summary>
        /// See <see cref="Constants.AccessStatus"/>
        /// </summary>
        public string AccessStatus { get; set; }

        public bool? AccountEnabled { get; set; }

        /// <summary>
        /// Id of the app role assignment on the enterprise app, while access is active
        /// </summary>
        public string? AppRoleAssignmentId { get; set; }

        public DateTimeOffset? InvitedAt { get; set; }

        /// <summary>
        /// Name of the admin who sent the latest invitation
        /// </summary>
        public string? InvitedBy { get; set; }

        public DateTimeOffset? LastSyncedAt { get; set; }

        public int? UserId { get; set; }
        public User? User { get; set; }

        public class Configuration : AuditableEntityConfiguration<ManagedUser>
        {
            public override void Configure(EntityTypeBuilder<ManagedUser> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.ManagedUserId);
                builder.Property(e => e.EntraObjectId).HasMaxLength(64).IsRequired();
                builder.Property(e => e.Email).HasMaxLength(320).IsRequired();
                builder.Property(e => e.DisplayName).HasMaxLength(256).IsRequired();
                builder.Property(e => e.UserType).HasMaxLength(16);
                builder.Property(e => e.InvitationStatus).HasMaxLength(32).IsRequired();
                builder.Property(e => e.AccessStatus).HasMaxLength(16).IsRequired();
                builder.Property(e => e.AppRoleAssignmentId).HasMaxLength(128);
                builder.Property(e => e.InvitedBy).HasMaxLength(256);

                builder.HasIndex(["EntraObjectId", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_ManagedUser_EntraObjectId");

                builder.HasIndex(["Email", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_ManagedUser_Email");

                // SQL Server filters a unique index on a nullable column to non-null values, so many rows can be unlinked
                builder.HasOne(e => e.User)
                    .WithOne()
                    .HasForeignKey<ManagedUser>(e => e.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                builder.HasIndex(e => e.UserId)
                    .IsUnique()
                    .HasDatabaseName("AK_ManagedUser_UserId");

                builder.ToTable("ManagedUser");
            }
        }
    }
}
