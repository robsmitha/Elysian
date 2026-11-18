using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    public class TenantModuleService(
        ElysianContext context,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        IMemoryCache cache,
        IOptions<ModuleSettings> options) : ITenantModuleService
    {
        public async Task<bool> IsEnabledAsync(string module, CancellationToken cancellationToken = default)
        {
            if (!options.Value.Enforce)
            {
                return true;
            }

            return (await GetActiveModulesAsync(cancellationToken)).Contains(module);
        }

        public async Task<IReadOnlySet<string>> GetActiveModulesAsync(CancellationToken cancellationToken = default)
        {
            var key = CacheKey();
            if (cache.TryGetValue(key, out IReadOnlySet<string>? cached) && cached != null)
            {
                return cached;
            }

            var rows = await context.TenantModules
                .AsNoTracking()
                .Where(m => m.Status == TenantModuleStatus.Active || m.Status == TenantModuleStatus.Trial)
                .Select(m => m.ModuleKey)
                .ToListAsync(cancellationToken);

            IReadOnlySet<string> modules = new HashSet<string>(rows, StringComparer.OrdinalIgnoreCase);
            cache.Set(key, modules, TimeSpan.FromSeconds(Math.Max(1, options.Value.CacheSeconds)));

            return modules;
        }

        public void Invalidate() => cache.Remove(CacheKey());

        /// <summary>
        /// Keyed by tenant: the cache is shared by every tenant the host serves
        /// </summary>
        private string CacheKey()
        {
            var tenantId = multiTenantContextAccessor.MultiTenantContext?.TenantInfo?.Id
                ?? throw new InvalidOperationException("Modules can only be read inside a tenant.");

            return $"tenant-modules:{tenantId}";
        }
    }
}
