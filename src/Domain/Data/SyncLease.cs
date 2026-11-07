using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// One sync per bank at a time. Linking a bank queues a first sync and Plaid's webhook queues another straight away;
    /// on separate Function instances both would start from no cursor and store every transaction twice.
    /// </summary>
    public class SyncLease
    {
        public int InstitutionAccessItemId { get; set; }
        /// <summary>Held until this time; a crashed sync's lease simply runs out</summary>
        public DateTimeOffset LeasedUntil { get; set; }
        public Guid Holder { get; set; }

        public class Configuration : IEntityTypeConfiguration<SyncLease>
        {
            public void Configure(EntityTypeBuilder<SyncLease> builder)
            {
                builder.HasKey(l => l.InstitutionAccessItemId);
                builder.Property(l => l.InstitutionAccessItemId).ValueGeneratedNever();
                // Two syncs taking an expired lease at once: only the first save wins
                builder.Property(l => l.LeasedUntil).IsConcurrencyToken();
                builder.Property(l => l.Holder).IsConcurrencyToken();
                builder.HasOne<InstitutionAccessItem>().WithMany().HasForeignKey(l => l.InstitutionAccessItemId).OnDelete(DeleteBehavior.Cascade);
                builder.ToTable("SyncLease").IsMultiTenant();
            }
        }
    }
}
