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

        public class Configuration : IEntityTypeConfiguration<OAuthToken>
        {
            public void Configure(EntityTypeBuilder<OAuthToken> builder)
            {
                builder.HasKey(k => k.OAuthTokenId);
                builder.Property(e => e.OAuthProvider).IsRequired();
                builder.Property(e => e.AccessToken).IsRequired();
                builder.Property(e => e.TokenType).IsRequired();
                builder.Property(e => e.UserId).IsRequired();
                builder.HasIndex(e => new { e.OAuthProvider, e.UserId }).IsUnique().HasDatabaseName("AK_OAuthProvider_UserId");

                builder.ToTable("OAuthToken");
            }
        }
    }
}
