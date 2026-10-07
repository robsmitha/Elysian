namespace Elysian.Domain.Constants
{
    /// <summary>
    /// Invitation states, matching Graph's externalUserState for guests
    /// </summary>
    public static class InvitationStatus
    {
        public const string PendingAcceptance = "PendingAcceptance";
        public const string Accepted = "Accepted";

        /// <summary>
        /// Members of the tenant are never invited
        /// </summary>
        public const string NotApplicable = "NotApplicable";
    }

    public static class AccessStatus
    {
        public const string Active = "Active";
        public const string Revoked = "Revoked";
    }

    /// <summary>
    /// Graph's userType values
    /// </summary>
    public static class EntraUserType
    {
        public const string Guest = "Guest";
        public const string Member = "Member";
    }

    public static class UserAuditAction
    {
        public const string Invited = "Invited";
        public const string InviteResent = "InviteResent";
        public const string Updated = "Updated";
        public const string AccessRevoked = "AccessRevoked";
        public const string AccessRestored = "AccessRestored";
        public const string Deleted = "Deleted";
        public const string Linked = "Linked";
        public const string Relinked = "Relinked";
    }
}
