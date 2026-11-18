using Elysian.Domain.Data;

namespace Elysian.Application.Interfaces
{
    public enum EdgeAccess
    {
        Allowed,
        MissingCredential,
        InvalidCredential,
        UnknownTenant,
        TenantNotAllowed,
        TenantSuspended
    }

    /// <param name="ClientName">The client that presented a valid key, even when the tenant was refused</param>
    public record EdgeAccessResult(EdgeAccess Access, string? ClientName = null, EdgeClientScope? Scope = null)
    {
        public bool IsAllowed => Access == EdgeAccess.Allowed;

        /// <summary>
        /// The admin app may name any tenant, so the caller must also check the signed-in user belongs to it
        /// </summary>
        public bool RequiresMembership => Scope == EdgeClientScope.Admin;
    }

    public interface IEdgeClientValidator
    {
        /// <summary>
        /// Whether the client holding <paramref name="credential"/> may act for <paramref name="tenantIdentifier"/>.
        /// Hosts call this before trusting a tenant named by the edge.
        /// </summary>
        Task<EdgeAccessResult> AuthorizeAsync(string? credential, string? tenantIdentifier, CancellationToken cancellationToken = default);
    }
}
