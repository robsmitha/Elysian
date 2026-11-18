namespace Elysian.Application.Interfaces
{
    public interface ITenantModuleService
    {
        /// <summary>
        /// Whether the current tenant may use a module. Always true while module enforcement is switched off.
        /// </summary>
        Task<bool> IsEnabledAsync(string module, CancellationToken cancellationToken = default);

        /// <summary>
        /// Modules the current tenant has switched on, whether or not enforcement is on
        /// </summary>
        Task<IReadOnlySet<string>> GetActiveModulesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Drops the current tenant's cached modules after they change
        /// </summary>
        void Invalidate();
    }
}
