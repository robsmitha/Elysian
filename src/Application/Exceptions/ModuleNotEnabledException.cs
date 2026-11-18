namespace Elysian.Application.Exceptions
{
    /// <summary>
    /// The tenant doesn't have the module a request belongs to. Derives from <see cref="ForbiddenAccessException"/>
    /// so hosts that don't know about modules still answer 403.
    /// </summary>
    public class ModuleNotEnabledException(string module) : ForbiddenAccessException
    {
        public string Module { get; } = module;

        public override string Message => $"The {Module} module isn't enabled for this site.";
    }
}
