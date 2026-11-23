using Elysian.Application.Features.SiteContent;
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
    ///
    /// A site drawn with a shared template stores no manifest of its own. Its manifest is the template's, made
    /// out for the site each time it is read: its name, its primary address, and the owner's own names for the
    /// categories. Nothing is copied, so it can't go stale when any of those change.
    /// </summary>
    public class SiteManifestReader(
        ElysianContext context,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        IMemoryCache cache,
        IOptions<TenantSettingsOptions> options,
        SiteTemplateCatalog? templates = null)
    {
        /// <summary>
        /// Where a manifest came from when it is the template's and not one the site stored
        /// </summary>
        public const string TemplateSource = "template";

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

            manifest ??= await FromTemplateAsync(multiTenantContextAccessor.MultiTenantContext!.TenantInfo!, cancellationToken);

            // "No manifest" is cached too, so a tenant without one doesn't cost a query per request
            cache.Set(key, new CacheEntry(manifest), TimeSpan.FromSeconds(Math.Max(1, options.Value.CacheSeconds)));
            return manifest;
        }

        private async Task<StoredSiteManifest?> FromTemplateAsync(ElysianTenantInfo tenant, CancellationToken cancellationToken)
        {
            var template = templates?.Find(tenant.TemplateKey, tenant.TemplateVersion);
            if (template?.ManifestJson == null)
            {
                return null;
            }

            var primaryHost = await context.TenantDomains.AsNoTracking()
                .Where(d => d.IsPrimary)
                .Select(d => d.Host)
                .FirstOrDefaultAsync(cancellationToken);

            var draft = await context.SiteContentDrafts.AsNoTracking()
                .Select(d => new { d.Json, d.ModifiedAt })
                .FirstOrDefaultAsync(cancellationToken);

            // The owner's own category names, from what they are editing: this is what the admin app shows
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (draft != null)
            {
                try
                {
                    foreach (var category in SiteContentDocument.FromJson(draft.Json).Categories)
                    {
                        if (!string.IsNullOrWhiteSpace(category.Name))
                        {
                            names[category.Slug] = category.Name.Trim();
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // A draft that can't be read costs the owner's names, not the site's photos
                }
            }

            // A site with no address yet still has photos to manage. ".invalid" never resolves, by design.
            var publicUrl = primaryHost != null ? $"https://{primaryHost}" : $"https://{tenant.Identifier}.invalid";
            var siteName = string.IsNullOrWhiteSpace(tenant.Name) ? tenant.Identifier! : tenant.Name;

            var json = template.ManifestFor(siteName, publicUrl, names)!;
            try
            {
                return new StoredSiteManifest(SiteManifestDocument.FromJson(json), json, TemplateSource, draft?.ModifiedAt ?? DateTimeOffset.UnixEpoch);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"The {template.Key} template's manifest can't be used for this site: {ex.Message}", ex);
            }
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
