using Elysian.Domain.Constants;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.UserManagement.Models
{
    public record ManagedUserModel(
        int Id,
        string Email,
        string DisplayName,
        string EntraObjectId,
        string? UserType,
        string InvitationStatus,
        string AccessStatus,
        bool? AccountEnabled,
        bool HasAssignment,
        DateTimeOffset? InvitedAt,
        string? InvitedBy,
        DateTimeOffset? LastSyncedAt,
        int? UserId)
    {
        public bool IsGuest => UserType == EntraUserType.Guest;
        public bool CanResendInvite => IsGuest && InvitationStatus == Domain.Constants.InvitationStatus.PendingAcceptance
            && AccessStatus == Domain.Constants.AccessStatus.Active;
        public bool CanDelete => IsGuest;

        public static ManagedUserModel From(ManagedUser m) => new(m.ManagedUserId, m.Email, m.DisplayName, m.EntraObjectId, m.UserType,
            m.InvitationStatus, m.AccessStatus, m.AccountEnabled, m.AppRoleAssignmentId != null, m.InvitedAt, m.InvitedBy,
            m.LastSyncedAt, m.UserId);
    }

    /// <param name="Created">False when the email was already managed (a re-invite)</param>
    /// <param name="InvitationEmailSent">False for members and guests who already accepted (nothing to redeem),
    /// or when sending failed; resend the invitation to retry</param>
    public record InviteUserResult(ManagedUserModel User, bool Created, bool InvitationEmailSent);

    public static class OnboardingOutcome
    {
        public const string WouldInvite = "WouldInvite";
        public const string Invited = "Invited";
        public const string Assigned = "Assigned";
        public const string AlreadyOnboarded = "AlreadyOnboarded";
        public const string Skipped = "Skipped";
        public const string Failed = "Failed";
    }

    public record OnboardingReportItem(int UserId, string UserName, string IdentityProvider, string? Email, string Outcome, string? Detail);
}
