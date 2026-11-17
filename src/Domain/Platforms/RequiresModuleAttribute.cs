namespace Elysian.Domain.Platforms
{
    /// <summary>
    /// Marks a request as belonging to a module, overriding the module its feature folder implies.
    /// Pass null for a request that must stay available to every tenant.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class RequiresModuleAttribute(string? module) : Attribute
    {
        public string? Module { get; } = module;
    }
}
