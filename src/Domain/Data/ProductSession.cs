using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// Session details for a <see cref="Product"/> of type <see cref="Constants.ProductTypes.Session"/>.
    /// The product holds the name, description, price and URL slug (<see cref="Product.SerialNumber"/>).
    /// Soft-deleting the product hides its session, since every query goes through the product.
    /// </summary>
    public class ProductSession
    {
        public int ProductId { get; set; }
        public Product Product { get; set; }

        public int DurationMinutes { get; set; }

        /// <summary>
        /// Shown on the booking pages, e.g. "Tallahassee, FL — location of your choice"
        /// </summary>
        public string? Location { get; set; }

        /// <summary>
        /// Investment page section the session is listed under, e.g. "Portraits"
        /// </summary>
        public string Collection { get; set; }

        /// <summary>
        /// What's included, listed on the Investment page
        /// </summary>
        public List<string> Features { get; set; } = [];

        /// <summary>
        /// Portfolio category slug, e.g. "maternity". Pre-selects the Contact form and supplies a fallback cover photo.
        /// </summary>
        public string? PortfolioCategory { get; set; }

        /// <summary>
        /// Photo library image used as the booking cover
        /// </summary>
        public Guid? CoverPhotoId { get; set; }
        public Photo? CoverPhoto { get; set; }

        /// <summary>
        /// Order within the collection, and of collections by their first session
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// False lists the session without online booking (e.g. weddings), so clients are sent to the Contact page instead
        /// </summary>
        public bool IsBookable { get; set; } = true;

        public class Configuration : IEntityTypeConfiguration<ProductSession>
        {
            public void Configure(EntityTypeBuilder<ProductSession> builder)
            {
                builder.IsMultiTenant();

                builder.HasKey(k => k.ProductId);
                builder.Property(e => e.Collection).IsRequired().HasMaxLength(100);
                builder.Property(e => e.Location).HasMaxLength(200);
                builder.Property(e => e.PortfolioCategory).HasMaxLength(64);

                builder.HasOne(e => e.Product)
                    .WithOne()
                    .HasForeignKey<ProductSession>(e => e.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(e => e.CoverPhoto)
                    .WithMany()
                    .HasForeignKey(e => e.CoverPhotoId)
                    .OnDelete(DeleteBehavior.SetNull);

                builder.ToTable("ProductSession");
            }
        }
    }
}
