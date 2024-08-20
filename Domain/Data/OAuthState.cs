using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    public class OAuthState
    {
        public int OAuthStateId { get; set; }
        public string OAuthProvider { get; set; }
        public string UserId { get; set; }
        public string State { get; set; }
        public DateTime CreatedAt { get; set; }

        public class Configuration : IEntityTypeConfiguration<OAuthState>
        {
            public void Configure(EntityTypeBuilder<OAuthState> builder)
            {
                builder.HasKey(k => k.OAuthStateId);
                builder.Property(e => e.OAuthProvider).IsRequired();
                builder.Property(e => e.UserId).IsRequired();
                builder.Property(e => e.State).IsRequired();

                builder.ToTable("OAuthState");
            }
        }
    }
}
