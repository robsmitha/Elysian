namespace Elysian.Domain.Security
{
    /// <summary>
    /// The request needs a signed-in person but not a place in any tenant, e.g. redeeming an invite or listing
    /// the sites someone belongs to. Use <see cref="AuthorizeAttribute"/> for anything inside a tenant.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class RequiresSignInAttribute : Attribute
    {
    }
}
