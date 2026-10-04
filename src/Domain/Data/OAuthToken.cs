using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    public class OAuthToken
    {
        public int OAuthTokenId { get; set; }
        public string OAuthProvider { get; set; }
        public string AccessToken { get; set; }
        public string TokenType { get; set; }
        public string Scope { get; set; }
        public string UserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Long-lived token used to get new access tokens, for providers that issue one (Google). Secret: never log or return it.
        /// </summary>
        public string? RefreshToken { get; set; }

        /// <summary>
        /// When the token last worked against the provider's API (e.g. a scheduled health check)
        /// </summary>
        public DateTime? LastValidatedAt { get; set; }

        public class Configuration : IEntityTypeConfiguration<OAuthToken>
        {
            public void Configure(EntityTypeBuilder<OAuthToken> builder)
            {
                builder.IsMultiTenant();

                builder.HasKey(k => k.OAuthTokenId);
                builder.Property(e => e.OAuthProvider).IsRequired();
                builder.Property(e => e.AccessToken).IsRequired();
                builder.Property(e => e.TokenType).IsRequired();
                builder.Property(e => e.UserId).IsRequired();
                builder.HasIndex(["OAuthProvider", "UserId", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_OAuthProvider_UserId");

                builder.ToTable("OAuthToken");
            }
        }
    }
}
