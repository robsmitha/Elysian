namespace Elysian.Domain.Security
{
    /// <summary>
    /// Only the people who run the platform may send this, and it needs no tenant: creating a tenant,
    /// suspending one, issuing an edge credential. Who they are is set in the host's configuration.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class RequiresPlatformAdminAttribute : Attribute
    {
    }
}
