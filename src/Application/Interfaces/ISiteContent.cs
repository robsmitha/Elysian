namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Where published site content goes so public pages can be drawn without asking this backend: a store at
    /// the edge, read by the Worker that serves cloud sites.
    /// </summary>
    public interface ISiteContentEdge
    {
        /// <summary>
        /// Writes one published version where the edge can read it. A version is written once and never
        /// changed. Throws when it could not be written; nothing may point at a version until this returns.
        /// </summary>
        Task WriteVersionAsync(string tenantIdentifier, int version, string templateKey, int templateVersion, string json,
            CancellationToken cancellationToken);

        /// <summary>
        /// Makes <paramref name="version"/> the one visitors get, for every address the tenant answers at
        /// </summary>
        Task PointToVersionAsync(string tenantIdentifier, int version, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Short-lived tokens that let a site's unpublished content be read without signing in, so an owner can see
    /// their draft on the public site. A token is good for one tenant until it expires and grants nothing else.
    /// </summary>
    public interface ISitePreviewTokens
    {
        (string Token, DateTimeOffset ExpiresAt) Issue(string tenantIdentifier);

        /// <summary>
        /// False when the token is malformed, was issued for another tenant, has been altered, or has expired
        /// </summary>
        bool IsValid(string tenantIdentifier, string? token);
    }
}
