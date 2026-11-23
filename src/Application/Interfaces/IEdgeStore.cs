namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// A key-value store at the edge, read by the Workers that serve public sites so a page never waits on this
    /// backend. Values are small JSON documents. Writes spread to every location within about a minute.
    /// </summary>
    public interface IEdgeConfigStore
    {
        /// <summary>
        /// False when this host has no edge store. A host that serves only sites with their own code needs none.
        /// </summary>
        bool IsConfigured { get; }

        /// <exception cref="InvalidOperationException">The store isn't configured, or refused the write</exception>
        Task PutAsync(string key, string json, CancellationToken cancellationToken);

        /// <summary>
        /// Removing a key that isn't there succeeds
        /// </summary>
        Task DeleteAsync(string key, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The edge's routing table: which tenant a public address belongs to and which version of its content to
    /// serve. It is built from the tenant's recorded addresses and can be rebuilt from them at any time. Only
    /// sites drawn with a shared template are in it; a site with its own code is served by its own Worker.
    /// </summary>
    public interface IEdgeHostMap
    {
        /// <summary>
        /// Writes an entry for every address the tenant has, from what is recorded now. Does nothing for a
        /// site with its own code, or on a host with no edge store.
        /// </summary>
        Task SyncAsync(string tenantIdentifier, CancellationToken cancellationToken);

        /// <summary>
        /// Stops an address answering. Called before the address's record is removed.
        /// </summary>
        Task RemoveAsync(string host, CancellationToken cancellationToken);
    }
}
