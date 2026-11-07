using Elysian.Domain.Constants;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// One of a person's spending categories. Each person has their own list (it used to be one global list); the
    /// defaults carry a <see cref="SystemKey"/> so Plaid's categories can land in them whatever they're renamed to.
    /// </summary>
    public class FinancialCategory : AuditableEntity
    {
        public int FinancialCategoryId { get; set; }
        public string Name { get; set; }
        /// <summary>The owner's external user id, like Budget.UserId</summary>
        public string UserId { get; set; }
        /// <summary>A default category's stable key (groceries, dining, ...); null for ones the person made up</summary>
        public string? SystemKey { get; set; }
        public string? Icon { get; set; }
        public CategoryGroup Group { get; set; } = CategoryGroup.Flexible;
        public int SortOrder { get; set; }
        /// <summary>Hidden from pickers; past transactions keep it</summary>
        public bool IsArchived { get; set; }

        public class Configuration : AuditableEntityConfiguration<FinancialCategory>
        {
            public override void Configure(EntityTypeBuilder<FinancialCategory> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.FinancialCategoryId);
                builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
                builder.Property(e => e.UserId).IsRequired().HasMaxLength(200);
                builder.Property(e => e.SystemKey).HasMaxLength(40);
                builder.Property(e => e.Icon).HasMaxLength(60);
                builder.Property(e => e.Group).HasConversion<string>().HasMaxLength(16).IsRequired();

                builder.HasIndex(e => new { e.UserId, e.SystemKey });

                builder.ToTable("FinancialCategory").IsMultiTenant();
            }
        }
    }
}
