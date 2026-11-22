using Elysian.Application.Features.Sites;

namespace Elysian.Application.Features.Placements
{
    /// <summary>
    /// The current tenant's placement slots, or null when it has none. Everything that publishes or reports on
    /// placements asks here, so each tenant gets its own catalog.
    /// </summary>
    public interface IPlacementCatalogSource
    {
        Task<PlacementCatalog?> GetAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The catalog a host was started with, for tenants that have no site manifest. A host that serves one site
    /// passes its catalog to <c>AddPlacementFeatures</c>; a host that serves many passes none, so a tenant
    /// without a manifest publishes nothing instead of another site's slots.
    /// </summary>
    public sealed record HostPlacementCatalog(PlacementCatalog? Catalog);

    /// <summary>
    /// The tenant's site manifest decides. A tenant with a manifest uses the manifest's slots, even when it
    /// lists none; only a tenant with no manifest at all falls back to the host's catalog.
    /// </summary>
    public sealed class TenantPlacementCatalogSource(SiteManifestReader manifests, HostPlacementCatalog host) : IPlacementCatalogSource
    {
        public async Task<PlacementCatalog?> GetAsync(CancellationToken cancellationToken = default)
        {
            var manifest = await manifests.GetAsync(cancellationToken);
            return manifest != null ? manifest.Document.Placements : host.Catalog;
        }
    }

    /// <summary>
    /// One fixed catalog, for tests and tools
    /// </summary>
    public sealed class FixedPlacementCatalogSource(PlacementCatalog? catalog) : IPlacementCatalogSource
    {
        public Task<PlacementCatalog?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(catalog);
    }
}
