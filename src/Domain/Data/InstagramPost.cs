using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// A recent post from the tenant's connected Instagram account, with thumbnails re-hosted in the public
    /// photos container. Rows are a mirror kept by SyncInstagramPostsCommand: added, updated and removed
    /// to match Instagram, so they're hard-deleted rather than audited.
    /// </summary>
    public class InstagramPost
    {
        public int InstagramPostId { get; set; }

        /// <summary>
        /// Instagram media id; also the storage folder for the thumbnails
        /// </summary>
        public string MediaId { get; set; }

        public string Permalink { get; set; }
        public string? Caption { get; set; }

        /// <summary>
        /// IMAGE, VIDEO or CAROUSEL_ALBUM
        /// </summary>
        public string MediaType { get; set; }

        public DateTimeOffset? PostedAt { get; set; }

        /// <summary>
        /// Pixel dimensions of the image the thumbnails were made from
        /// </summary>
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>
        /// Tiny blurred preview as a data URI, rendered while the real thumbnail loads
        /// </summary>
        public string? Placeholder { get; set; }

        public List<int> VariantWidths { get; set; } = [];

        /// <summary>
        /// Last time this post was seen in Instagram's response
        /// </summary>
        public DateTimeOffset SyncedAt { get; set; }

        public class Configuration : IEntityTypeConfiguration<InstagramPost>
        {
            public void Configure(EntityTypeBuilder<InstagramPost> builder)
            {
                builder.IsMultiTenant();

                builder.HasKey(k => k.InstagramPostId);

                builder.Property(e => e.MediaId).IsRequired().HasMaxLength(64);
                builder.HasIndex(["MediaId", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_InstagramPost_MediaId");

                builder.Property(e => e.Permalink).IsRequired().HasMaxLength(512);
                builder.Property(e => e.MediaType).IsRequired().HasMaxLength(32);

                builder.ToTable("InstagramPost");
            }
        }
    }
}
