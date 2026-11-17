using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    public class EdgeClientValidator(
        PlatformContext context,
        IMultiTenantStore<ElysianTenantInfo> tenantStore,
        IMemoryCache cache,
        TimeProvider timeProvider,
        IOptions<EdgeSettings> options) : IEdgeClientValidator
    {
        public async Task<EdgeAccessResult> AuthorizeAsync(string? credential, string? tenantIdentifier, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(credential))
            {
                return new EdgeAccessResult(EdgeAccess.MissingCredential);
            }

            if (!EdgeClientKeys.TryParse(credential, out var name, out var key))
            {
                return new EdgeAccessResult(EdgeAccess.InvalidCredential);
            }

            var client = await FindAsync(name, key, cancellationToken);
            if (client == null)
            {
                return new EdgeAccessResult(EdgeAccess.InvalidCredential);
            }

            if (string.IsNullOrWhiteSpace(tenantIdentifier))
            {
                return new EdgeAccessResult(EdgeAccess.UnknownTenant, client.Name, client.Scope);
            }

            var tenant = await tenantStore.TryGetByIdentifierAsync(tenantIdentifier);
            if (tenant == null)
            {
                return new EdgeAccessResult(EdgeAccess.UnknownTenant, client.Name, client.Scope);
            }

            if (!Covers(client, tenant))
            {
                return new EdgeAccessResult(EdgeAccess.TenantNotAllowed, client.Name, client.Scope);
            }

            // The admin app can still open a suspended site, to export it or bring it back
            var suspended = string.Equals(tenant.Status, TenantStatusNames.Suspended, StringComparison.OrdinalIgnoreCase);
            return new EdgeAccessResult(suspended && client.Scope != EdgeClientScope.Admin ? EdgeAccess.TenantSuspended : EdgeAccess.Allowed,
                client.Name, client.Scope);
        }

        private static bool Covers(CachedClient client, ElysianTenantInfo tenant) => client.Scope switch
        {
            EdgeClientScope.Tenant => !string.IsNullOrEmpty(client.TenantIdentifier)
                && string.Equals(client.TenantIdentifier, tenant.Identifier, StringComparison.OrdinalIgnoreCase),

            // A platform's shared template serves its cloud tenants only; a custom site has its own client
            EdgeClientScope.Platform => !string.IsNullOrEmpty(client.Platform)
                && string.Equals(client.Platform, tenant.Platform, StringComparison.OrdinalIgnoreCase)
                && string.Equals(tenant.Runtime, RuntimeNames.Cloud, StringComparison.OrdinalIgnoreCase),

            EdgeClientScope.Admin => true,
            _ => false
        };

        /// <summary>
        /// The enabled client with this name, if the key is its current or previous one
        /// </summary>
        private async Task<CachedClient?> FindAsync(string name, string key, CancellationToken cancellationToken)
        {
            var cacheKey = $"edge-client:{name}";
            var now = timeProvider.GetUtcNow();

            if (cache.TryGetValue(cacheKey, out CachedClient? cached) && cached != null)
            {
                if (cached.Accepts(key))
                {
                    return cached;
                }

                if (now - cached.LoadedAt < TimeSpan.FromSeconds(options.Value.RecheckAfterSeconds))
                {
                    return null;
                }
            }

            var loaded = await context.EdgeClients
                .AsNoTracking()
                .Where(c => c.Name == name)
                .Select(c => new CachedClient(c.Name, c.Scope, c.TenantIdentifier, c.Platform, c.KeyHash, c.PreviousKeyHash, c.IsEnabled, now))
                .FirstOrDefaultAsync(cancellationToken)
                ?? CachedClient.Missing(name, now);

            // An unknown name is cached too, so guessing names doesn't cost a query each
            cache.Set(cacheKey, loaded, TimeSpan.FromSeconds(Math.Max(1, options.Value.CacheSeconds)));

            return loaded.Accepts(key) ? loaded : null;
        }

        private sealed record CachedClient(string Name, EdgeClientScope Scope, string? TenantIdentifier, string? Platform,
            string? KeyHash, string? PreviousKeyHash, bool IsEnabled, DateTimeOffset LoadedAt)
        {
            public static CachedClient Missing(string name, DateTimeOffset now) => new(name, default, null, null, null, null, false, now);

            public bool Accepts(string key)
            {
                // Both are checked every time so the timing doesn't show which one matched
                var current = EdgeClientKeys.Matches(key, KeyHash);
                var previous = EdgeClientKeys.Matches(key, PreviousKeyHash);
                return IsEnabled && (current || previous);
            }
        }
    }
}
