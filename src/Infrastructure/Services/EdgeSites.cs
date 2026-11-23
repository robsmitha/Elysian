using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// The keys the edge store holds, and what is in them. The Worker that serves cloud sites reads exactly
    /// these, so a change here is a change there.
    /// </summary>
    public static class EdgeKeys
    {
        /// <summary>
        /// "host:{host}": which tenant an address belongs to and what to serve for it. JSON:
        /// <c>{ tenant, status, template, templateVersion, contentVersion, primaryHost, modules }</c>.
        /// contentVersion is null until the site has published; primaryHost is where every other address
        /// redirects to.
        /// </summary>
        public static string Host(string host) => $"host:{host}";

        /// <summary>
        /// "content:{tenant}:{version}": one published version of a site's content, never changed once
        /// written. JSON: <c>{ tenant, version, template, templateVersion, content }</c>.
        /// </summary>
        public static string Content(string tenantIdentifier, int version) => $"content:{tenantIdentifier}:{version}";
    }

    /// <summary>
    /// Published site content and the host map, written to the edge store. Order matters to visitors: a
    /// version is written before anything points at it, and an address is removed from the edge before its
    /// record is.
    /// </summary>
    public class EdgeSites(IEdgeConfigStore store, PlatformContext platform, ILogger<EdgeSites> logger) : ISiteContentEdge, IEdgeHostMap
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task WriteVersionAsync(string tenantIdentifier, int version, string templateKey, int templateVersion, string json,
            CancellationToken cancellationToken)
        {
            using var content = JsonDocument.Parse(json);
            var document = JsonSerializer.Serialize(new
            {
                tenant = tenantIdentifier,
                version,
                template = templateKey,
                templateVersion,
                content = content.RootElement,
            }, JsonOptions);

            await store.PutAsync(EdgeKeys.Content(tenantIdentifier, version), document, cancellationToken);
        }

        public Task PointToVersionAsync(string tenantIdentifier, int version, CancellationToken cancellationToken) =>
            WriteHostsAsync(tenantIdentifier, version, required: true, cancellationToken);

        public Task SyncAsync(string tenantIdentifier, CancellationToken cancellationToken) =>
            WriteHostsAsync(tenantIdentifier, contentVersion: null, required: false, cancellationToken);

        public async Task RemoveAsync(string host, CancellationToken cancellationToken)
        {
            if (!store.IsConfigured)
            {
                return;
            }
            await store.DeleteAsync(EdgeKeys.Host(host), cancellationToken);
        }

        /// <param name="contentVersion">The version to point at, or null to use the one recorded on the tenant</param>
        /// <param name="required">A publish must reach the edge; keeping the map in step after another change only needs to when there is an edge</param>
        private async Task WriteHostsAsync(string tenantIdentifier, int? contentVersion, bool required, CancellationToken cancellationToken)
        {
            var tenant = await platform.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Identifier == tenantIdentifier, cancellationToken)
                ?? throw new InvalidOperationException($"There is no site named {tenantIdentifier}.");

            if (!required && (!store.IsConfigured || tenant.Runtime != RuntimeNames.Cloud))
            {
                return;
            }

            var domains = await platform.TenantDomains.AsNoTracking()
                .Where(d => d.TenantId == tenant.Id)
                .ToListAsync(cancellationToken);

            if (domains.Count == 0)
            {
                // Nothing to point: the content is at the edge and is served as soon as the site has an address
                logger.LogWarning("{Tenant} has no addresses, so nothing at the edge points at its content yet", tenantIdentifier);
                return;
            }

            var primaryHost = domains.FirstOrDefault(d => d.IsPrimary)?.Host ?? domains.OrderBy(d => d.Host, StringComparer.Ordinal).First().Host;

            // What the site may show: a page that belongs to a module is only drawn for a site that has it
            var modules = (await platform.TenantModules.AsNoTracking()
                    .Where(m => m.TenantId == tenant.Id && m.Status != TenantModuleStatus.Suspended)
                    .Select(m => m.ModuleKey)
                    .ToListAsync(cancellationToken))
                .Order(StringComparer.Ordinal)
                .ToList();
            var entry = JsonSerializer.Serialize(new
            {
                tenant = tenant.Identifier,
                status = tenant.Status ?? TenantStatusNames.Active,
                template = tenant.TemplateKey,
                templateVersion = tenant.TemplateVersion,
                contentVersion = contentVersion ?? tenant.ContentVersion,
                primaryHost,
                modules,
            }, JsonOptions);

            // The primary address last: if a write fails partway, the address visitors are sent to still
            // shows the previous version, never a mix
            foreach (var domain in domains.OrderBy(d => d.IsPrimary).ThenBy(d => d.Host, StringComparer.Ordinal))
            {
                await store.PutAsync(EdgeKeys.Host(domain.Host), entry, cancellationToken);
            }
        }
    }

    /// <summary>
    /// The edge store as a Cloudflare Workers KV namespace, written through Cloudflare's API:
    /// PUT and DELETE /accounts/{account}/storage/kv/namespaces/{namespace}/values/{key}.
    /// </summary>
    public class CloudflareKvEdgeStore(HttpClient httpClient, IOptions<EdgeStoreSettings> options, ILogger<CloudflareKvEdgeStore> logger)
        : IEdgeConfigStore
    {
        private const int MaxAttempts = 3;
        private const int MaxKeyBytes = 512;

        private readonly EdgeStoreSettings settings = options.Value;

        public bool IsConfigured => settings.IsConfigured;

        public Task PutAsync(string key, string json, CancellationToken cancellationToken) =>
            SendAsync(HttpMethod.Put, key, json, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
            SendAsync(HttpMethod.Delete, key, null, cancellationToken);

        private async Task SendAsync(HttpMethod method, string key, string? value, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    $"{EdgeStoreSettings.SectionName}:AccountId, NamespaceId and ApiToken are not all configured, so nothing can be written to the edge.");
            }
            if (Encoding.UTF8.GetByteCount(key) > MaxKeyBytes || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                throw new InvalidOperationException("That is not a key the edge store can hold.");
            }

            var path = $"accounts/{Uri.EscapeDataString(settings.AccountId!)}/storage/kv/namespaces/{Uri.EscapeDataString(settings.NamespaceId!)}/values/{Uri.EscapeDataString(key)}";

            for (var attempt = 1; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);
                if (value != null)
                {
                    request.Content = new StringContent(value, Encoding.UTF8, "application/octet-stream");
                }

                HttpStatusCode? status = null;
                try
                {
                    using var response = await httpClient.SendAsync(request, cancellationToken);
                    status = response.StatusCode;

                    // A key that was never there is as gone as one just removed
                    if (response.IsSuccessStatusCode || (method == HttpMethod.Delete && status == HttpStatusCode.NotFound))
                    {
                        return;
                    }
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "Edge store {Method} {Key}: attempt {Attempt} could not reach Cloudflare", method, key, attempt);
                }

                // Worth another try only when the fault may pass: no answer, too many requests, or Cloudflare's own error
                var retry = status is null or HttpStatusCode.TooManyRequests || (int?)status >= 500;
                if (!retry || attempt >= MaxAttempts)
                {
                    // The response body is left out on purpose: it is of no use to an owner, and nothing
                    // from the request, the token least of all, belongs in an error
                    throw new InvalidOperationException(status == null
                        ? "The edge store could not be reached."
                        : $"The edge store refused the change (HTTP {(int)status}).");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
            }
        }
    }
}
