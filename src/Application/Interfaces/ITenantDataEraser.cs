namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Removes a tenant's rows from the database for good: every table that carries a TenantId, the tenant's
    /// merchant record, any edge client made for it alone, and last the tenant itself. Not a soft delete.
    /// Safe to run again after a failure: what is already gone stays gone and the rest is removed.
    /// </summary>
    public interface ITenantDataEraser
    {
        /// <returns>How many rows were removed</returns>
        Task<int> EraseAsync(string tenantId, string tenantIdentifier, CancellationToken cancellationToken = default);
    }
}
