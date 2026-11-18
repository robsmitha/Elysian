using Elysian.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Context
{
    /// <summary>
    /// Reads data that belongs to the platform, not to a tenant. <see cref="ElysianContext"/> fixes its tenant
    /// when it's created, so it can't be touched before the request's tenant is known; this context has no
    /// tenant and is safe to use while working out who the caller is, and for the few jobs that span tenants.
    /// The tables are created by
    /// ElysianContext's migrations. Nothing here is audited automatically, so code that writes sets the audit columns.
    /// </summary>
    public class PlatformContext(DbContextOptions<PlatformContext> options) : DbContext(options)
    {
        public DbSet<EdgeClient> EdgeClients { get; set; }
        public DbSet<Merchant> Merchants { get; set; }
        public DbSet<MerchantType> MerchantTypes { get; set; }
        public DbSet<ElysianTenantInfo> Tenants { get; set; }
        public DbSet<TenantInvite> TenantInvites { get; set; }
        public DbSet<TenantMembership> TenantMemberships { get; set; }
        public DbSet<TenantModule> TenantModules { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new EdgeClient.Configuration());
            modelBuilder.Entity<EdgeClient>().HasQueryFilter(e => !e.IsDeleted);

            // The tenant store's table, so a new tenant and everything it needs are saved in one transaction
            modelBuilder.Entity<ElysianTenantInfo>(builder =>
            {
                builder.HasKey(t => t.Id);
                builder.Property(t => t.Id).HasMaxLength(64);
                builder.Property(t => t.Identifier).HasMaxLength(450);
                builder.Property(t => t.Platform).HasMaxLength(64);
                builder.Property(t => t.Runtime).HasMaxLength(64);
                builder.Property(t => t.Status).HasMaxLength(64);
                builder.ToTable("TenantInfo");
            });

            modelBuilder.ApplyConfiguration(new MerchantType.Configuration());
            modelBuilder.Entity<MerchantType>().HasQueryFilter(e => !e.IsDeleted);
            modelBuilder.ApplyConfiguration(new Merchant.Configuration());
            modelBuilder.Entity<Merchant>().HasQueryFilter(e => !e.IsDeleted);

            // Tenant-owned tables read across tenants on purpose: finding a person's sites, redeeming an invite
            modelBuilder.Entity<TenantMembership>(builder =>
            {
                TenantMembership.Configuration.ConfigureColumns(builder);
                builder.HasQueryFilter(e => !e.IsDeleted);
            });
            modelBuilder.Entity<TenantModule>(builder =>
            {
                TenantModule.Configuration.ConfigureColumns(builder);
                builder.HasQueryFilter(e => !e.IsDeleted);
            });
            modelBuilder.Entity<TenantInvite>(builder =>
            {
                TenantInvite.Configuration.ConfigureColumns(builder);
                builder.HasQueryFilter(e => !e.IsDeleted);
            });

            base.OnModelCreating(modelBuilder);
        }

        /// <summary>
        /// Fills the audit columns ElysianContext would fill by itself
        /// </summary>
        public static T Stamp<T>(T entity, string actor, DateTimeOffset now) where T : Elysian.Domain.Seedwork.AuditableEntity
        {
            entity.CreatedByUserId = actor;
            entity.CreatedAt = now;
            entity.ModifiedByUserId = actor;
            entity.ModifiedAt = now;
            return entity;
        }
    }
}
