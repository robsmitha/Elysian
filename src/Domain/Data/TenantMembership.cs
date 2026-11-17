using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Security.Cryptography;
using System.Text;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// Someone's place in a tenant: who they are comes from the sign-in provider, what they may do comes from here.
    /// A membership made from an email address has no <see cref="Subject"/> until that person first signs in.
    /// </summary>
    public class TenantMembership : AuditableEntity
    {
        public int TenantMembershipId { get; set; }

        /// <summary>
        /// Set by the tenant filter on save. Declared so platform-level code can read memberships across tenants.
        /// </summary>
        public string TenantId { get; set; }

        /// <summary>
        /// The sign-in provider's id for the person
        /// </summary>
        public string? Subject { get; set; }

        /// <summary>
        /// Lowercase. Matches a first sign-in to a membership created before the person had an account.
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// <see cref="Security.RoleNames.Owner"/> or <see cref="Security.RoleNames.Editor"/>
        /// </summary>
        public string Role { get; set; }

        public class Configuration : AuditableEntityConfiguration<TenantMembership>
        {
            public override void Configure(EntityTypeBuilder<TenantMembership> builder)
            {
                base.Configure(builder);
                ConfigureColumns(builder);

                builder.IsMultiTenant();
            }

            /// <summary>
            /// Shared with PlatformContext, which maps the same table without the tenant filter
            /// </summary>
            public static void ConfigureColumns(EntityTypeBuilder<TenantMembership> builder)
            {
                builder.HasKey(k => k.TenantMembershipId);

                builder.Property(e => e.TenantId).IsRequired().HasMaxLength(64);
                builder.Property(e => e.Subject).HasMaxLength(128);
                builder.Property(e => e.Email).HasMaxLength(320);
                builder.Property(e => e.Role).IsRequired().HasMaxLength(32);

                builder.HasIndex(["Subject", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_TenantMembership_Subject")
                    .HasFilter($"[{nameof(Subject)}] IS NOT NULL AND [{nameof(IsDeleted)}] = 0");

                builder.HasIndex(["Email", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_TenantMembership_Email")
                    .HasFilter($"[{nameof(Email)}] IS NOT NULL AND [{nameof(IsDeleted)}] = 0");

                builder.ToTable("TenantMembership");
            }
        }
    }

    /// <summary>
    /// A one-time code that makes whoever redeems it a member of the tenant. Only the code's hash is stored.
    /// </summary>
    public class TenantInvite : AuditableEntity
    {
        public int TenantInviteId { get; set; }

        public string TenantId { get; set; }

        public string CodeHash { get; set; }

        public string Role { get; set; }

        /// <summary>
        /// When set, only someone signed in with this address may redeem the code
        /// </summary>
        public string? Email { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset? RedeemedAt { get; set; }

        public string? RedeemedBySubject { get; set; }

        public DateTimeOffset? RevokedAt { get; set; }

        public bool CanBeRedeemedAt(DateTimeOffset now) => RedeemedAt == null && RevokedAt == null && ExpiresAt > now;

        public class Configuration : AuditableEntityConfiguration<TenantInvite>
        {
            public override void Configure(EntityTypeBuilder<TenantInvite> builder)
            {
                base.Configure(builder);
                ConfigureColumns(builder);

                builder.IsMultiTenant();
            }

            public static void ConfigureColumns(EntityTypeBuilder<TenantInvite> builder)
            {
                builder.HasKey(k => k.TenantInviteId);

                builder.Property(e => e.TenantId).IsRequired().HasMaxLength(64);
                builder.Property(e => e.CodeHash).IsRequired().HasMaxLength(64);
                builder.Property(e => e.Role).IsRequired().HasMaxLength(32);
                builder.Property(e => e.Email).HasMaxLength(320);
                builder.Property(e => e.RedeemedBySubject).HasMaxLength(128);

                // Unique across every tenant: a code is redeemed before the tenant is known
                builder.HasIndex(e => e.CodeHash).IsUnique().HasDatabaseName("AK_TenantInvite_CodeHash");

                builder.ToTable("TenantInvite");
            }
        }
    }

    /// <summary>
    /// Invite codes look like "K7QF-2MXD-9RTA-HB4W": 80 random bits from letters and digits that are hard to misread
    /// </summary>
    public static class InviteCodes
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static string Generate()
        {
            var bytes = RandomNumberGenerator.GetBytes(16);
            var builder = new StringBuilder(19);
            for (var i = 0; i < bytes.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    builder.Append('-');
                }
                builder.Append(Alphabet[bytes[i] % Alphabet.Length]);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Uppercase with anything that isn't a letter or digit removed, so a code typed with spaces or in lowercase still works
        /// </summary>
        public static string Normalize(string? code) =>
            new string((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        public static string Hash(string? code) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));
    }
}
