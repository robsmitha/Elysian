using System.Text.Json;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;

namespace Elysian.Application.Features.Sites.Queries
{
    /// <summary>
    /// What the admin app needs to draw itself for one site
    /// </summary>
    /// <param name="Modules">The tenant's active modules, which decide the admin navigation</param>
    /// <param name="Manifest">The site's manifest as the site wrote it; null when it has none yet</param>
    /// <param name="ManifestStoredAt">Changes whenever the manifest does, so a client can tell its copy is stale</param>
    public sealed record SiteModel(
        string Identifier,
        string? Name,
        string? Platform,
        string? Runtime,
        IReadOnlyList<string> Modules,
        JsonElement? Manifest,
        DateTimeOffset? ManifestStoredAt);

    /// <summary>
    /// The current tenant's site, for anyone who manages it. Not for public pages: a site knows its own
    /// structure from its build and never asks for it at page load.
    /// </summary>
    [Authorize]
    public record GetSiteQuery : IRequest<SiteModel>;

    public class GetSiteQueryHandler(SiteManifestReader reader, ITenantModuleService modules,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor) : IRequestHandler<GetSiteQuery, SiteModel>
    {
        public async Task<SiteModel> Handle(GetSiteQuery request, CancellationToken cancellationToken)
        {
            var tenant = multiTenantContextAccessor.MultiTenantContext.TenantInfo!;
            var manifest = await reader.GetAsync(cancellationToken);
            var active = await modules.GetActiveModulesAsync(cancellationToken);

            JsonElement? document = null;
            if (manifest != null)
            {
                using var parsed = JsonDocument.Parse(manifest.Json);
                document = parsed.RootElement.Clone();
            }

            return new SiteModel(
                tenant.Identifier!,
                tenant.Name,
                tenant.Platform,
                tenant.Runtime,
                [.. active.Order(StringComparer.Ordinal)],
                document,
                manifest?.StoredAt);
        }
    }
}
