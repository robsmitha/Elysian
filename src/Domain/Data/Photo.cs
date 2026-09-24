using Elysian.Domain.Constants;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    public class Photo : AuditableEntity
    {
        /// <summary>
        /// Also the storage folder for the original and its variants, so it must never be reused.
        /// </summary>
        public Guid PhotoId { get; set; }

        /// <summary>
        /// Portfolio category slug, e.g. "weddings"
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Optional named placement, e.g. "hero-1" or "aspenPortrait", unique per tenant
        /// </summary>
        public string? Slot { get; set; }
        public int SortOrder { get; set; }
        public string? AltText { get; set; }

        public string OriginalFileName { get; set; }
        public string OriginalBlobName { get; set; }
        public long OriginalFileSize { get; set; }

        /// <summary>
        /// Oriented pixel dimensions of the original, known once processing completes
        /// </summary>
        public int? Width { get; set; }
        public int? Height { get; set; }

        /// <summary>
        /// Focal point used for cover crops, 0..1 from the top-left
        /// </summary>
        public double FocusX { get; set; } = 0.5;
        public double FocusY { get; set; } = 0.5;

        /// <summary>
        /// Tiny blurred preview as a data URI, rendered while the real variant loads
        /// </summary>
        public string? Placeholder { get; set; }

        /// <summary>
        /// Bumped on every reprocess so variant URLs stay immutable
        /// </summary>
        public int Version { get; set; }
        public List<int> VariantWidths { get; set; } = [];

        public PhotoStatus Status { get; set; }
        public string? ProcessingError { get; set; }

        public class Configuration : AuditableEntityConfiguration<Photo>
        {
            public override void Configure(EntityTypeBuilder<Photo> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.PhotoId);
                builder.Property(e => e.PhotoId).ValueGeneratedNever();

                builder.Property(e => e.Category).IsRequired().HasMaxLength(64);
                builder.Property(e => e.Slot).HasMaxLength(64);
                builder.HasIndex(["Slot", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_Photo_Slot")
                    .HasFilter($"[{nameof(Slot)}] IS NOT NULL AND [{nameof(IsDeleted)}] = 0");
                builder.HasIndex(["Category", "SortOrder"]).HasDatabaseName("IX_Photo_Category_SortOrder");

                builder.Property(e => e.AltText).HasMaxLength(512);
                builder.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(256);
                builder.Property(e => e.OriginalBlobName).IsRequired().HasMaxLength(512);
                builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
                builder.Property(e => e.ProcessingError).HasMaxLength(2048);

                builder.ToTable("Photo");
            }
        }
    }
}
