using Elysian.Application.Exceptions;
using Elysian.Application.Features.UserManagement;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Elysian.Infrastructure.Identity
{
    public partial class IdentityService
    {
        public const string PlatformAdminAccessAction = "PlatformAdminAccess";

        public bool IsPlatformAdmin(ClaimsPrincipal? principal)
        {
            var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrWhiteSpace(subject)
                && authOptions?.Value.PlatformAdminSubjects.Contains(subject, StringComparer.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Roles come from the person's membership of this tenant, not from the sign-in token: the same person can
        /// own one site and have no place in another. Someone with no membership is refused, unless they run the
        /// platform.
        /// </summary>
        private async Task<User> GetOrCreateFromMembershipAsync(ClaimsPrincipal principal)
        {
            var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new ForbiddenAccessException();
            }

            var email = EmailOf(principal);
            var isPlatformAdmin = IsPlatformAdmin(principal);

            var membership = await context.TenantMemberships.FirstOrDefaultAsync(m => m.Subject == subject);
            if (membership == null && email != null)
            {
                // Added by email before they had an account: this sign-in claims it
                membership = await context.TenantMemberships.FirstOrDefaultAsync(m => m.Subject == null && m.Email == email);
                if (membership != null)
                {
                    membership.Subject = subject;
                }
            }

            if (membership == null && !isPlatformAdmin)
            {
                throw new ForbiddenAccessException();
            }

            List<string> roles = isPlatformAdmin ? [RoleNames.SystemAdmin] : [membership!.Role];
            var userName = principal.FindFirst(ClaimTypes.Name)?.Value ?? email ?? subject;
            var identityProvider = principal.FindFirst(SignInClaims.IdentityProvider)?.Value ?? SignInClaims.ExternalIdProvider;

            var user = await context.Users.FirstOrDefaultAsync(u => u.ExternalUserId == subject);
            var firstAccess = user == null;

            if (user == null && membership != null && email != null)
            {
                user = await MoveExistingUserAsync(email, subject, identityProvider);
            }

            if (user == null)
            {
                user = new User
                {
                    ExternalUserId = subject,
                    UserName = userName,
                    Email = email,
                    IdentityProvider = identityProvider,
                    AccessControl = new AccessControl { Roles = roles, Policies = PolicyNames.GetDefaultPolicies(roles) }
                };
                await context.AddAsync(user);
            }
            else if (!SameRoles(user.AccessControl?.Roles, roles))
            {
                // The membership changed since they last signed in. Policies restart from the new role's defaults.
                user.AccessControl = new AccessControl { Roles = roles, Policies = PolicyNames.GetDefaultPolicies(roles) };
            }

            if (isPlatformAdmin && membership == null)
            {
                logger.LogInformation("Platform admin {Subject} is acting in tenant {Tenant}", subject, tenantInfo.Identifier);
                if (firstAccess)
                {
                    context.UserAuditLogs.Add(new UserAuditLog
                    {
                        ActorUserId = subject,
                        ActorName = userName,
                        Action = PlatformAdminAccessAction,
                        TargetEmail = email,
                        Details = "A platform admin acted in this tenant for the first time",
                        OccurredAt = timeProvider.GetUtcNow()
                    });
                }
            }

            if (context.ChangeTracker.HasChanges())
            {
                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateException) when (firstAccess)
                {
                    // A page makes several requests at once, and on someone's first visit each of them tries to
                    // create the person. The database lets one through; this one lost. Use the winner's row.
                    context.ChangeTracker.Clear();
                    var created = await context.Users.FirstOrDefaultAsync(u => u.ExternalUserId == subject);
                    if (created == null)
                    {
                        // Not that race: something else stopped the save
                        throw;
                    }

                    return created;
                }
            }

            return user;
        }

        /// <summary>
        /// The tenant's user from before the sign-in provider changed, found by the email their membership was
        /// made for, moved to the new sign-in id along with their data. Null when there isn't exactly one.
        /// </summary>
        private async Task<User?> MoveExistingUserAsync(string email, string subject, string identityProvider)
        {
            var candidates = await context.Users
                .Where(u => u.ExternalUserId != subject && (u.Email == email || u.UserName.ToLower() == email))
                .Take(2)
                .ToListAsync();

            if (candidates.Count != 1)
            {
                return null;
            }

            var user = candidates[0];
            await MoveUserDataAsync(user.ExternalUserId, subject);

            logger.LogInformation("Moved user {UserId} to a new sign-in id on their first membership sign-in", user.UserId);
            user.ExternalUserId = subject;
            user.IdentityProvider = identityProvider;
            user.Email ??= email;

            return user;
        }

        /// <summary>
        /// Rows that store the sign-in id directly, rather than the user's key, follow the user to a new id.
        /// Loaded and changed one by one (rather than ExecuteUpdate) so it runs in the same transaction as the user.
        /// </summary>
        private async Task MoveUserDataAsync(string previousId, string newId)
        {
            foreach (var budget in await context.Budgets.Where(b => b.UserId == previousId).ToListAsync())
            {
                budget.UserId = newId;
            }
            foreach (var bill in await context.BillTrackings.Where(b => b.UserId == previousId).ToListAsync())
            {
                bill.UserId = newId;
            }
            foreach (var token in await context.OAuthTokens.Where(t => t.UserId == previousId && t.OAuthProvider == OAuthProviders.GitHub).ToListAsync())
            {
                token.UserId = newId;
            }
        }

        private static string? EmailOf(ClaimsPrincipal principal)
        {
            var candidate = principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;
            return UserManagementService.LooksLikeEmail(candidate) ? UserManagementService.NormalizeEmail(candidate!) : null;
        }

        private static bool SameRoles(List<string>? current, List<string> expected) =>
            current != null && current.Count == expected.Count
            && !current.Except(expected, StringComparer.OrdinalIgnoreCase).Any();
    }
}
