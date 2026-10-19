using Elysian.Domain.Constants;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// One wedding, event or other session told as a page: a hero (title plus optional details) and an
    /// ordered set of photos. The photos are ordinary <see cref="Photo"/> rows with <see cref="Photo.StoryId"/> set.
    /// </summary>
    public class Story : AuditableEntity
    {
        public Guid StoryId { get; set; }

        /// <summary>
        /// URL segment, unique per tenant, e.g. "emily-and-james-goodwood-museum"
        /// </summary>
        public string Slug { get; set; }

        /// <summary>
        /// Portfolio category slug, e.g. "weddings"; the story's photos share it
        /// </summary>
        public string Category { get; set; }

        public string Title { get; set; }
        public string? Description { get; set; }
        public DateOnly? EventDate { get; set; }
        public string? VenueName { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }

        /// <summary>
        /// One of this story's photos; when null the first photo is used
        /// </summary>
        public Guid? HeroPhotoId { get; set; }
        public Photo? HeroPhoto { get; set; }

        public StoryStatus Status { get; set; }

        /// <summary>
        /// First time the story was published; the list's fallback sort when there's no event date
        /// </summary>
        public DateTimeOffset? PublishedAt { get; set; }

        public string? SeoTitle { get; set; }
        public string? SeoDescription { get; set; }

        public List<Photo> Photos { get; set; } = [];

        public class Configuration : AuditableEntityConfiguration<Story>
        {
            public override void Configure(EntityTypeBuilder<Story> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.StoryId);
                builder.Property(e => e.StoryId).ValueGeneratedNever();

                builder.Property(e => e.Slug).IsRequired().HasMaxLength(128);
                builder.HasIndex(["Slug", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_Story_Slug")
                    .HasFilter($"[{nameof(IsDeleted)}] = 0");

                builder.Property(e => e.Category).IsRequired().HasMaxLength(64);
                builder.HasIndex(["Category", "Status"]).HasDatabaseName("IX_Story_Category_Status");

                builder.Property(e => e.Title).IsRequired().HasMaxLength(160);
                builder.Property(e => e.Description).HasMaxLength(2000);
                builder.Property(e => e.VenueName).HasMaxLength(160);
                builder.Property(e => e.City).HasMaxLength(100);
                builder.Property(e => e.State).HasMaxLength(64);
                builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
                builder.Property(e => e.SeoTitle).HasMaxLength(70);
                builder.Property(e => e.SeoDescription).HasMaxLength(160);

                // Both sides are soft-deleted, so neither FK cascades
                builder.HasOne(b => b.HeroPhoto)
                    .WithMany()
                    .HasForeignKey(b => b.HeroPhotoId)
                    .OnDelete(DeleteBehavior.Restrict);

                builder.HasMany(b => b.Photos)
                    .WithOne(p => p.Story)
                    .HasForeignKey(p => p.StoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                builder.ToTable("Story");
            }
        }
    }
}
