using Elysian.Application.Exceptions;
using Elysian.Application.Features.UserManagement.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.UserManagement
{
    /// <summary>
    /// Gives people access to the app through Entra ID: B2B guest invitations, app role assignments on the enterprise
    /// app, and a local <see cref="ManagedUser"/> record per person. Callers authorize (the commands require user.write).
    /// </summary>
    /// <remarks>
    /// Invitation redeem URLs go only into the invitee's email: they're never logged, persisted, audited or returned.
    /// </remarks>
    public class UserManagementService(
        ElysianContext context,
        IEntraDirectoryService directory,
        IEmailService emailService,
        IIdentityService identityService,
        IClaimsPrincipalAccessor claimsPrincipalAccessor,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        IOptions<EntraSettings> entraSettings,
        TimeProvider timeProvider,
        ILogger<UserManagementService> logger)
    {
        /// <summary>
        /// Lists older than this are refreshed from Graph when read
        /// </summary>
        public static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(15);

        private const string IdentityProviderAad = "aad";

        public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        public async Task<InviteUserResult> InviteAsync(string email, string displayName, bool sendInvitationEmail = true,
            CancellationToken cancellationToken = default)
        {
            var normalizedEmail = NormalizeEmail(email);
            var name = displayName.Trim();
            var now = timeProvider.GetUtcNow();

            var row = await context.ManagedUsers.FirstOrDefaultAsync(m => m.Email == normalizedEmail, cancellationToken);

            // Search lags behind writes, so fall back to the object id we already know
            var entraUser = await directory.FindUserByEmailAsync(normalizedEmail, cancellationToken);
            if (entraUser == null && row != null)
            {
                entraUser = await directory.GetUserAsync(row.EntraObjectId, cancellationToken);
            }

            // Members sign in with their own account and guests who accepted already can; only pending or new guests get an invitation.
            EntraInvitation? invitation = null;
            string objectId;
            if (entraUser != null && (entraUser.UserType == EntraUserType.Member || entraUser.ExternalUserState == InvitationStatus.Accepted))
            {
                objectId = entraUser.Id;
            }
            else
            {
                // Graph returns the existing guest for an address it already knows, so this never duplicates a user.
                invitation = await directory.InviteGuestAsync(normalizedEmail, name, cancellationToken);
                objectId = invitation.UserId;
            }

            var assignment = await directory.AssignAsync(objectId, cancellationToken);

            row ??= await context.ManagedUsers.FirstOrDefaultAsync(m => m.EntraObjectId == objectId, cancellationToken);
            var created = row == null;
            if (row == null)
            {
                row = new ManagedUser();
                context.ManagedUsers.Add(row);
            }

            row.EntraObjectId = objectId;
            row.Email = normalizedEmail;
            row.DisplayName = name;
            row.UserType = entraUser?.UserType ?? EntraUserType.Guest;
            row.InvitationStatus = StatusFor(row.UserType, entraUser?.ExternalUserState ?? InvitationStatus.PendingAcceptance);
            row.AccountEnabled = entraUser?.AccountEnabled ?? true;
            row.AccessStatus = AccessStatus.Active;
            row.AppRoleAssignmentId = assignment.Id;
            row.LastSyncedAt = now;
            if (invitation != null)
            {
                row.InvitedAt = now;
                row.InvitedBy = ActorName;
            }

            await LinkUserAsync(row, cancellationToken);

            // Saved before auditing so a new row has its id
            await context.SaveChangesAsync(cancellationToken);

            var details = (created ? "Created" : "Re-invited")
                + (invitation == null ? $"; {row.UserType?.ToLowerInvariant()} already able to sign in, no invitation sent" : "");
            Audit(UserAuditAction.Invited, row, details);
            await context.SaveChangesAsync(cancellationToken);

            var emailSent = false;
            if (invitation != null && sendInvitationEmail)
            {
                try
                {
                    await SendInvitationAsync(row, invitation, cancellationToken);
                    emailSent = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The user is invited and assigned; the admin can resend. The exception never holds the URL.
                    logger.LogError(ex, "Invitation email for managed user {ManagedUserId} could not be sent", row.ManagedUserId);
                }
            }

            return new InviteUserResult(ManagedUserModel.From(row), created, emailSent);
        }

        public async Task<ManagedUserModel> ResendInviteAsync(int id, CancellationToken cancellationToken = default)
        {
            var row = await FindAsync(id, cancellationToken);

            if (row.UserType == EntraUserType.Member)
            {
                throw new ConflictException($"{row.Email} is a member of your organization and doesn't need an invitation.");
            }
            if (row.AccessStatus == AccessStatus.Revoked)
            {
                throw new ConflictException("Restore this user's access before resending the invitation.");
            }

            var entraUser = await directory.GetUserAsync(row.EntraObjectId, cancellationToken)
                ?? throw new ConflictException($"{row.Email} no longer exists in Entra ID. Delete them here and invite them again.");

            Apply(row, entraUser);
            if (row.InvitationStatus == InvitationStatus.Accepted)
            {
                await context.SaveChangesAsync(cancellationToken);
                throw new ConflictException($"{row.Email} has already accepted the invitation.");
            }

            var invitation = await directory.InviteGuestAsync(row.Email, row.DisplayName, cancellationToken);
            if (!string.Equals(invitation.UserId, row.EntraObjectId, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Re-invitation for managed user {ManagedUserId} returned a different Entra object", row.ManagedUserId);
                row.EntraObjectId = invitation.UserId;
            }

            await SendInvitationAsync(row, invitation, cancellationToken);

            row.InvitedAt = timeProvider.GetUtcNow();
            row.InvitedBy = ActorName;
            Audit(UserAuditAction.InviteResent, row);
            await context.SaveChangesAsync(cancellationToken);

            return ManagedUserModel.From(row);
        }

        /// <summary>
        /// Local records with their cached Entra state, synced first when <paramref name="refresh"/> is set or the
        /// cache is older than <see cref="SyncInterval"/>. A sync is one assignments listing plus $batch user lookups.
        /// </summary>
        public async Task<List<ManagedUserModel>> ListAsync(bool refresh = false, CancellationToken cancellationToken = default)
        {
            var rows = await context.ManagedUsers.OrderBy(m => m.DisplayName).ToListAsync(cancellationToken);

            var staleBefore = timeProvider.GetUtcNow() - SyncInterval;
            if (rows.Count > 0 && (refresh || rows.Any(r => r.LastSyncedAt == null || r.LastSyncedAt < staleBefore)))
            {
                try
                {
                    await SyncAsync(rows, cancellationToken);
                }
                catch (GraphApiException ex) when (!refresh)
                {
                    // A background refresh shouldn't hide the list; LastSyncedAt shows how old it is.
                    logger.LogWarning(ex, "Managed user sync failed; returning cached state");
                }
            }

            return rows.Select(ManagedUserModel.From).ToList();
        }

        public async Task<ManagedUserModel> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var row = await FindAsync(id, cancellationToken);

            try
            {
                var entraUser = await directory.GetUserAsync(row.EntraObjectId, cancellationToken);
                var assignment = entraUser == null ? null : await directory.FindAssignmentAsync(row.EntraObjectId, cancellationToken);
                Apply(row, entraUser, assignment);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (GraphApiException ex)
            {
                logger.LogWarning(ex, "Refreshing managed user {ManagedUserId} failed; returning cached state", row.ManagedUserId);
            }

            return ManagedUserModel.From(row);
        }

        /// <summary>
        /// Updates the display name locally, and in Entra for guests we invited. Members' directory profiles belong to
        /// the organization, so they're left alone.
        /// </summary>
        public async Task<ManagedUserModel> UpdateAsync(int id, string displayName, CancellationToken cancellationToken = default)
        {
            var row = await FindAsync(id, cancellationToken);
            var name = displayName.Trim();

            if (name != row.DisplayName)
            {
                if (row.UserType == EntraUserType.Guest)
                {
                    await directory.UpdateDisplayNameAsync(row.EntraObjectId, name, cancellationToken);
                }

                row.DisplayName = name;
                Audit(UserAuditAction.Updated, row, "Display name changed");
                await context.SaveChangesAsync(cancellationToken);
            }

            return ManagedUserModel.From(row);
        }

        /// <summary>
        /// Removes the user's assignment to the enterprise app, so they can no longer sign in. The Entra account is kept.
        /// </summary>
        public async Task<ManagedUserModel> RevokeAccessAsync(int id, CancellationToken cancellationToken = default)
        {
            var row = await FindAsync(id, cancellationToken);
            await EnsureNotSelfAsync(row, "revoke your own access", cancellationToken);

            await RemoveAssignmentsAsync(row, cancellationToken);

            row.AccessStatus = AccessStatus.Revoked;
            row.AppRoleAssignmentId = null;
            Audit(UserAuditAction.AccessRevoked, row);
            await context.SaveChangesAsync(cancellationToken);

            return ManagedUserModel.From(row);
        }

        public async Task<ManagedUserModel> RestoreAccessAsync(int id, CancellationToken cancellationToken = default)
        {
            var row = await FindAsync(id, cancellationToken);

            var entraUser = await directory.GetUserAsync(row.EntraObjectId, cancellationToken)
                ?? throw new ConflictException($"{row.Email} no longer exists in Entra ID. Delete them here and invite them again.");

            var assignment = await directory.AssignAsync(row.EntraObjectId, cancellationToken);
            Apply(row, entraUser, assignment);
            Audit(UserAuditAction.AccessRestored, row);
            await context.SaveChangesAsync(cancellationToken);

            return ManagedUserModel.From(row);
        }

        /// <summary>
        /// Revokes access, deletes the guest account from the tenant and removes the local record. Refuses members:
        /// their accounts belong to the organization.
        /// </summary>
        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var row = await FindAsync(id, cancellationToken);
            await EnsureNotSelfAsync(row, "delete yourself", cancellationToken);

            var entraUser = await directory.GetUserAsync(row.EntraObjectId, cancellationToken);
            if (entraUser != null && entraUser.UserType != EntraUserType.Guest)
            {
                throw new ConflictException($"{row.Email} is a member of your organization, not a guest, so their account can't be deleted here. Revoke their access instead.");
            }
            if (entraUser == null && row.UserType == EntraUserType.Member)
            {
                // Can't confirm the type; never risk deleting a member
                throw new ConflictException($"{row.Email} couldn't be found in Entra ID to confirm they're a guest. Revoke their access instead.");
            }

            if (entraUser != null)
            {
                await RemoveAssignmentsAsync(row, cancellationToken);
                await directory.DeleteUserAsync(row.EntraObjectId, cancellationToken);
            }

            Audit(UserAuditAction.Deleted, row, entraUser == null ? "Already gone from Entra ID" : "Guest deleted from Entra ID");
            context.ManagedUsers.Remove(row);
            await context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Puts everyone who has signed in with Microsoft (aad) under management: invites them as guests (or assigns
        /// existing members) so they keep access once sign-in requires assignment. <paramref name="dryRun"/> only reports.
        /// </summary>
        public async Task<List<OnboardingReportItem>> OnboardExistingUsersAsync(bool dryRun, bool sendInvitationEmails,
            CancellationToken cancellationToken = default)
        {
            var users = await context.Users.AsNoTracking().OrderBy(u => u.UserId).ToListAsync(cancellationToken);
            var managed = await context.ManagedUsers.AsNoTracking().ToListAsync(cancellationToken);

            var report = new List<OnboardingReportItem>();
            foreach (var user in users)
            {
                var email = EmailFor(user);
                OnboardingReportItem Item(string outcome, string? detail = null) =>
                    new(user.UserId, user.UserName, user.IdentityProvider, email, outcome, detail);

                if (!string.Equals(user.IdentityProvider, IdentityProviderAad, StringComparison.OrdinalIgnoreCase))
                {
                    report.Add(Item(OnboardingOutcome.Skipped, $"Signs in with {user.IdentityProvider}. Ask them for a Microsoft account email and invite it."));
                    continue;
                }
                if (email == null)
                {
                    report.Add(Item(OnboardingOutcome.Skipped, "No email address on record."));
                    continue;
                }

                var existing = managed.FirstOrDefault(m => m.Email == email);
                if (existing?.AccessStatus == AccessStatus.Active)
                {
                    report.Add(Item(OnboardingOutcome.AlreadyOnboarded, existing.InvitationStatus));
                    continue;
                }
                if (existing?.AccessStatus == AccessStatus.Revoked)
                {
                    report.Add(Item(OnboardingOutcome.Skipped, "Access was revoked by an admin; restore it to onboard."));
                    continue;
                }
                if (dryRun)
                {
                    report.Add(Item(OnboardingOutcome.WouldInvite));
                    continue;
                }

                try
                {
                    var result = await InviteAsync(email, user.UserName, sendInvitationEmails, cancellationToken);
                    report.Add(Item(
                        result.User.UserType == EntraUserType.Member || result.User.InvitationStatus == InvitationStatus.Accepted
                            ? OnboardingOutcome.Assigned
                            : OnboardingOutcome.Invited,
                        sendInvitationEmails && result.User.InvitationStatus == InvitationStatus.PendingAcceptance && !result.InvitationEmailSent
                            ? "Invitation email failed; resend it from the user list."
                            : result.User.InvitationStatus));
                }
                catch (GraphApiException ex)
                {
                    logger.LogError(ex, "Onboarding user {UserId} failed", user.UserId);
                    report.Add(Item(OnboardingOutcome.Failed, ex.StatusCode is int status ? $"Microsoft Graph error {status}" : "Microsoft Graph error"));
                }
            }

            return report;
        }

        private async Task SyncAsync(List<ManagedUser> rows, CancellationToken cancellationToken)
        {
            var settings = entraSettings.Value;
            var assignments = (await directory.ListAssignmentsAsync(cancellationToken))
                .GroupBy(a => a.PrincipalId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(a => string.Equals(a.AppRoleId, settings.AppRoleId, StringComparison.OrdinalIgnoreCase) ? 0 : 1).First(),
                    StringComparer.OrdinalIgnoreCase);

            var entraUsers = await directory.GetUsersAsync(rows.Select(r => r.EntraObjectId), cancellationToken);

            foreach (var row in rows)
            {
                Apply(row, entraUsers.GetValueOrDefault(row.EntraObjectId), assignments.GetValueOrDefault(row.EntraObjectId));
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Copies Entra state onto the row. With <paramref name="refreshAssignment"/> (the default when an assignment
        /// lookup was made), a missing assignment or user means access is revoked.
        /// </summary>
        private void Apply(ManagedUser row, EntraUser? entraUser, EntraAppRoleAssignment? assignment, bool refreshAssignment = true)
        {
            if (entraUser != null)
            {
                row.UserType = entraUser.UserType ?? row.UserType;
                row.AccountEnabled = entraUser.AccountEnabled;
                row.InvitationStatus = StatusFor(row.UserType, entraUser.ExternalUserState ?? row.InvitationStatus);
            }
            else
            {
                row.AccountEnabled = null;
            }

            if (refreshAssignment)
            {
                row.AppRoleAssignmentId = entraUser == null ? null : assignment?.Id;
                row.AccessStatus = row.AppRoleAssignmentId == null ? AccessStatus.Revoked : AccessStatus.Active;
            }

            row.LastSyncedAt = timeProvider.GetUtcNow();
        }

        private void Apply(ManagedUser row, EntraUser entraUser) => Apply(row, entraUser, null, refreshAssignment: false);

        private static string StatusFor(string? userType, string externalUserState) =>
            userType == EntraUserType.Member ? InvitationStatus.NotApplicable
            : externalUserState == InvitationStatus.Accepted ? InvitationStatus.Accepted
            : InvitationStatus.PendingAcceptance;

        private async Task RemoveAssignmentsAsync(ManagedUser row, CancellationToken cancellationToken)
        {
            if (row.AppRoleAssignmentId != null)
            {
                await directory.RemoveAssignmentAsync(row.AppRoleAssignmentId, cancellationToken);
            }

            // Catch assignments made outside the app (e.g. in the portal) or with another role. Bounded in case Graph lags.
            for (var i = 0; i < 5; i++)
            {
                var other = await directory.FindAssignmentAsync(row.EntraObjectId, cancellationToken);
                if (other == null) return;
                await directory.RemoveAssignmentAsync(other.Id, cancellationToken);
            }
        }

        private async Task SendInvitationAsync(ManagedUser row, EntraInvitation invitation, CancellationToken cancellationToken)
        {
            var email = UserInviteEmails.Invitation(row.Email, row.DisplayName, AppName, invitation.RedeemUrl, ActorName);
            await emailService.SendAsync(email, cancellationToken);
        }

        /// <summary>
        /// Links the record to the person's <see cref="User"/> when they've signed in with Microsoft under the same email
        /// </summary>
        private async Task LinkUserAsync(ManagedUser row, CancellationToken cancellationToken)
        {
            if (row.UserId != null) return;

            var candidates = await context.Users
                .Where(u => u.IdentityProvider == IdentityProviderAad
                    && (u.Email == row.Email || u.UserName.ToLower() == row.Email))
                .Select(u => u.UserId)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (candidates.Count != 1) return;

            var userId = candidates[0];
            var taken = await context.ManagedUsers.AnyAsync(m => m.UserId == userId && m.ManagedUserId != row.ManagedUserId, cancellationToken);
            if (!taken)
            {
                row.UserId = userId;
            }
        }

        private async Task EnsureNotSelfAsync(ManagedUser row, string action, CancellationToken cancellationToken)
        {
            var actor = await identityService.GetOrCreateAsync(claimsPrincipalAccessor.Principal!);
            var actorEmail = EmailFor(actor);

            if ((row.UserId != null && row.UserId == actor.UserId) || (actorEmail != null && actorEmail == row.Email))
            {
                throw new ConflictException($"You can't {action}.");
            }
        }

        private async Task<ManagedUser> FindAsync(int id, CancellationToken cancellationToken) =>
            await context.ManagedUsers.FirstOrDefaultAsync(m => m.ManagedUserId == id, cancellationToken)
                ?? throw new NotFoundException(nameof(ManagedUser), id);

        private void Audit(string action, ManagedUser target, string? details = null)
        {
            context.UserAuditLogs.Add(new UserAuditLog
            {
                ActorUserId = claimsPrincipalAccessor.UserId ?? string.Empty,
                ActorName = ActorName,
                Action = action,
                TargetManagedUserId = target.ManagedUserId == 0 ? null : target.ManagedUserId,
                TargetEntraObjectId = target.EntraObjectId,
                TargetEmail = target.Email,
                Details = details,
                OccurredAt = timeProvider.GetUtcNow()
            });
        }

        /// <summary>
        /// The user's email, for Microsoft sign-ins: the Email column, else the user name SWA reported
        /// </summary>
        public static string? EmailFor(User user)
        {
            if (!string.Equals(user.IdentityProvider, IdentityProviderAad, StringComparison.OrdinalIgnoreCase)) return null;
            var candidate = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.UserName;
            return LooksLikeEmail(candidate) ? NormalizeEmail(candidate!) : null;
        }

        public static bool LooksLikeEmail(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value.Contains('@') && !value.Trim().Contains(' ');

        private string? ActorName => string.IsNullOrWhiteSpace(claimsPrincipalAccessor.UserName) ? null : claimsPrincipalAccessor.UserName;

        private string AppName
        {
            get
            {
                var tenantName = multiTenantContextAccessor.MultiTenantContext?.TenantInfo?.Name;
                if (!string.IsNullOrWhiteSpace(tenantName)) return tenantName;
                return Uri.TryCreate(entraSettings.Value.AppUrl, UriKind.Absolute, out var uri) ? uri.Host : "the app";
            }
        }
    }
}
