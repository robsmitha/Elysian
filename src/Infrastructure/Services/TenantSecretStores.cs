using Azure;
using Azure.Security.KeyVault.Secrets;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Per-tenant secrets from configuration, as TenantSecrets:{identifier}:{name}. For local development and
    /// hosts with a handful of tenants; app settings can point each one at a Key Vault reference.
    /// </summary>
    public class ConfigurationTenantSecretStore(IConfiguration configuration,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor) : ITenantSecretStore
    {
        public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            var identifier = multiTenantContextAccessor.MultiTenantContext?.TenantInfo?.Identifier;
            if (string.IsNullOrEmpty(identifier))
            {
                return Task.FromResult<string?>(null);
            }

            var value = configuration[$"{TenantSecretOptions.SectionName}:{identifier}:{name}"];
            return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    /// <summary>
    /// Per-tenant secrets from Key Vault, named tenant-{identifier}-{name}, so a tenant can be given a secret
    /// without changing the host's settings.
    /// </summary>
    public class KeyVaultTenantSecretStore(SecretClient client,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        IMemoryCache cache, IOptions<TenantSecretOptions> options) : ITenantSecretStore
    {
        public async Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            var identifier = multiTenantContextAccessor.MultiTenantContext?.TenantInfo?.Identifier;
            if (string.IsNullOrEmpty(identifier))
            {
                return null;
            }

            var secretName = SecretName(identifier, name);
            var key = $"tenant-secret:{secretName}";
            if (cache.TryGetValue(key, out CachedSecret? cached) && cached != null)
            {
                return cached.Value;
            }

            string? value;
            try
            {
                value = (await client.GetSecretAsync(secretName, cancellationToken: cancellationToken)).Value.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                value = null;
            }

            // A missing secret is cached too, so a tenant without one doesn't cost a Key Vault call per request
            cache.Set(key, new CachedSecret(value), TimeSpan.FromSeconds(Math.Max(1, options.Value.CacheSeconds)));

            return value;
        }

        /// <summary>
        /// Key Vault names allow only letters, digits and dashes
        /// </summary>
        public static string SecretName(string tenantIdentifier, string name) =>
            new string($"tenant-{tenantIdentifier}-{name}".Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());

        private sealed record CachedSecret(string? Value);
    }
}
