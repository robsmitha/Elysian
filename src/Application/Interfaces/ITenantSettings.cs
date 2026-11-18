namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// A settings class as the current tenant sees it: the host's configuration, with the tenant's own values
    /// laid over it. Use this instead of IOptions for anything that differs from one site to the next.
    /// </summary>
    public interface ITenantSettings<out T> where T : class
    {
        /// <summary>
        /// Shared between requests, so treat it as read-only. The first read for a tenant queries the database
        /// synchronously; after that it's served from memory.
        /// </summary>
        T Value { get; }
    }

    public interface ITenantSecretStore
    {
        /// <summary>
        /// A secret belonging to the current tenant, or null when the tenant has none by that name
        /// </summary>
        Task<string?> GetAsync(string name, CancellationToken cancellationToken = default);
    }

    public static class TenantSecretNames
    {
        public const string TurnstileSecretKey = "turnstile-secret-key";
    }
}
