using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// Assigns a photo to one spot on the site, e.g. "home-intro" or "contact-portrait".
    /// The site defines which spots exist (like a theme's image slots); this only stores
    /// what fills them. Each spot is independent, and one photo may fill many spots.
    /// </summary>
    public class Placement : AuditableEntity
    {
        public int PlacementId { get; set; }

        /// <summary>
        /// Spot key defined by the site, unique per tenant
        /// </summary>
        public string Key { get; set; }

        public Guid PhotoId { get; set; }
        public Photo Photo { get; set; }

        public class Configuration : AuditableEntityConfiguration<Placement>
        {
            public override void Configure(EntityTypeBuilder<Placement> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.PlacementId);

                builder.Property(e => e.Key).IsRequired().HasMaxLength(64);
                builder.HasIndex(["Key", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_Placement_Key")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                // Photos are soft-deleted, so the FK never cascades; DeletePhotoCommand clears placements
                builder.HasOne(b => b.Photo)
                    .WithMany()
                    .HasForeignKey(b => b.PhotoId)
                    .OnDelete(DeleteBehavior.Restrict);

                builder.ToTable("Placement");
            }
        }
    }
}
