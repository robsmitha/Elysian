using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Sites
{
    /// <summary>
    /// A stored manifest: the parsed document, and the exact JSON the site wrote
    /// </summary>
    public sealed record StoredSiteManifest(SiteManifestDocument Document, string Json, string Source, DateTimeOffset StoredAt);

    /// <summary>
    /// Reads the current tenant's site manifest, kept in memory for a short while (TenantSettings:CacheSeconds)
    /// so a burst of publish messages or admin requests reads the database once.
    ///
    /// A stored manifest that can't be read throws rather than counting as "no manifest": falling back to a
    /// host catalog, or to nothing, would quietly publish the wrong slots.
    /// </summary>
    public class SiteManifestReader(
        ElysianContext context,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        IMemoryCache cache,
        IOptions<TenantSettingsOptions> options)
    {
        private sealed record CacheEntry(StoredSiteManifest? Manifest);

        public static string CacheKey(string tenantId) => $"site-manifest:{tenantId}";

        /// <returns>Null when the tenant has no manifest, or when there's no tenant</returns>
        public async Task<StoredSiteManifest?> GetAsync(CancellationToken cancellationToken = default)
        {
            var tenantId = multiTenantContextAccessor.MultiTenantContext?.TenantInfo?.Id;
            if (string.IsNullOrEmpty(tenantId))
            {
                return null;
            }

            var key = CacheKey(tenantId);
            if (cache.TryGetValue(key, out CacheEntry? cached) && cached != null)
            {
                return cached.Manifest;
            }

            var row = await context.SiteManifests.AsNoTracking()
                .Select(m => new { m.Json, m.Source, m.ModifiedAt })
                .FirstOrDefaultAsync(cancellationToken);

            StoredSiteManifest? manifest = null;
            if (row != null)
            {
                try
                {
                    manifest = new StoredSiteManifest(SiteManifestDocument.FromJson(row.Json), row.Json, row.Source, row.ModifiedAt);
                }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException($"The stored site manifest for this tenant can't be used: {ex.Message}", ex);
                }
            }

            // "No manifest" is cached too, so a tenant without one doesn't cost a query per request
            cache.Set(key, new CacheEntry(manifest), TimeSpan.FromSeconds(Math.Max(1, options.Value.CacheSeconds)));
            return manifest;
        }

        /// <summary>
        /// After the current tenant's manifest changes
        /// </summary>
        public void Invalidate()
        {
            var tenantId = multiTenantContextAccessor.MultiTenantContext?.TenantInfo?.Id;
            if (!string.IsNullOrEmpty(tenantId))
            {
                cache.Remove(CacheKey(tenantId));
            }
        }
    }
}
