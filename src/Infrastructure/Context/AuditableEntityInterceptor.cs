using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Elysian.Application.Interfaces;
using Elysian.Domain.Seedwork;
using System.Linq.Expressions;

namespace Elysian.Infrastructure.Context
{
    public class AuditableEntityInterceptor(
        IClaimsPrincipalAccessor claimsPrincipalAccessor,
        TimeProvider dateTime) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            UpdateEntities(eventData.Context);

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            UpdateEntities(eventData.Context);

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public void UpdateEntities(DbContext? context)
        {
            if (context == null) return;

            foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
            {
                if (entry.State is EntityState.Added or EntityState.Modified || entry.HasChangedOwnedEntities())
                {
                    var utcNow = dateTime.GetUtcNow();
                    if (entry.State == EntityState.Added)
                    {
                        entry.Entity.CreatedByUserId = claimsPrincipalAccessor.UserId;
                        entry.Entity.CreatedAt = utcNow;
                    }
                    entry.Entity.ModifiedByUserId = claimsPrincipalAccessor.UserId;
                    entry.Entity.ModifiedAt = utcNow;
                }
            }
        }
    }

    public static class Extensions
    {
        public static bool HasChangedOwnedEntities(this EntityEntry entry) =>
            entry.References.Any(r =>
                r.TargetEntry != null &&
                r.TargetEntry.Metadata.IsOwned() &&
                (r.TargetEntry.State == EntityState.Added || r.TargetEntry.State == EntityState.Modified));

        public static void ApplyGlobalIsDeletedFilter(this ModelBuilder modelBuilder)
        {
            var auditableEntities = modelBuilder.Model.GetEntityTypes()
                .Where(et => typeof(AuditableEntity).IsAssignableFrom(et.ClrType));
            foreach (var entityType in auditableEntities)
            {
                var isDeletedProperty = entityType.FindProperty(nameof(AuditableEntity.IsDeleted));
                if (isDeletedProperty != null && isDeletedProperty.ClrType == typeof(bool))
                {
                    var entityTypeBuilder = modelBuilder.Entity(entityType.ClrType);
                    var parameter = Expression.Parameter(entityType.ClrType, "e");
                    var body = Expression.Not(Expression.Property(parameter, isDeletedProperty.PropertyInfo));
                    var lambda = Expression.Lambda(body, parameter);

                    entityTypeBuilder.HasQueryFilter(lambda);
                }
            }
        }

    }
}
