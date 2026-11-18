namespace Elysian.Infrastructure.Settings
{
    public enum RoleSource
    {
        /// <summary>
        /// Roles arrive with the signed-in principal, as Static Web Apps supplies them. A host serving one site.
        /// </summary>
        Principal = 0,

        /// <summary>
        /// Roles come from the person's membership of the request's tenant. A host serving many tenants.
        /// </summary>
        Membership = 1
    }

    /// <summary>
    /// Bound from the "Auth" configuration section
    /// </summary>
    public class AuthSettings
    {
        public const string SectionName = "Auth";

        public RoleSource RoleSource { get; set; } = RoleSource.Principal;

        /// <summary>
        /// The sign-in provider's issuer address; its signing keys are read from
        /// {Authority}/.well-known/openid-configuration
        /// </summary>
        public string? Authority { get; set; }

        /// <summary>
        /// The audience a token must be issued for: this API's application id
        /// </summary>
        public string? Audience { get; set; }

        /// <summary>
        /// Sign-in ids of the people who run the platform. They may act in any tenant with every permission,
        /// and can only be named here, never through an invite.
        /// </summary>
        public List<string> PlatformAdminSubjects { get; set; } = [];

        public int InviteLifetimeDays { get; set; } = 14;
    }
}
