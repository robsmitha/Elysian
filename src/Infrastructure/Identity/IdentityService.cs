using Elysian.Application.Exceptions;
using Elysian.Application.Features.UserManagement;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Elysian.Infrastructure.Identity
{
    public class IdentityService(ElysianContext context, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        TimeProvider timeProvider, ILogger<IdentityService> logger) : IIdentityService
    {
        private const string IdentityProviderAad = "aad";

        private readonly ElysianTenantInfo tenantInfo = multiTenantContextAccessor.MultiTenantContext.TenantInfo!;

        public Task<bool> AuthorizeAsync(User user, string policyName)
        {
            return Task.FromResult(user!.AccessControl.Policies.Contains(policyName, StringComparer.OrdinalIgnoreCase));
        }

        public Task<bool> IsInRoleAsync(User user, string roleName)
        {
            return Task.FromResult(user!.AccessControl.Roles.Contains(roleName, StringComparer.OrdinalIgnoreCase));
        }

        public async Task<User> GetOrCreateAsync(ClaimsPrincipal principal)
        {
            var merchant = await context.Merchants.FirstOrDefaultAsync(m => m.MerchantIdentifier == tenantInfo.Identifier)
                ?? throw new NotFoundException(tenantInfo.Identifier ?? "Merchant not found");

            var externalUserId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var user = await context.Users.FirstOrDefaultAsync(u => u.ExternalUserId == externalUserId);

            if (user != null)
            {
                return user;
            }

            var userName = principal?.FindFirst(ClaimTypes.Name)?.Value;
            var identityProvider = principal?.FindFirst("idp")?.Value;
            var email = string.Equals(identityProvider, IdentityProviderAad, StringComparison.OrdinalIgnoreCase)
                && UserManagementService.LooksLikeEmail(userName)
                    ? UserManagementService.NormalizeEmail(userName!)
                    : null;

            // Someone an admin put under management with this email. Only they are matched by email, since that's an
            // address the admin vetted and Entra requires them to be assigned before they can sign in.
            var managedUser = email == null ? null : await context.ManagedUsers
                .FirstOrDefaultAsync(m => m.Email == email && m.AccessStatus == AccessStatus.Active);

            if (managedUser != null && externalUserId != null)
            {
                var relinked = await RelinkAsync(managedUser, email!, externalUserId);
                if (relinked != null)
                {
                    return relinked;
                }
            }

            var roles = principal?.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList() ?? [];

            user = new User
            {
                ExternalUserId = externalUserId,
                UserName = userName,
                Email = email,
                IdentityProvider = identityProvider,
                AccessControl = new AccessControl
                {
                    Roles = roles,
                    Policies = PolicyNames.GetDefaultPolicies(roles)
                }
            };

            await context.AddAsync(user);
            await context.SaveChangesAsync();

            if (managedUser != null && managedUser.UserId == null)
            {
                managedUser.UserId = user.UserId;
                MarkAccepted(managedUser);
                Audit(UserAuditAction.Linked, managedUser, externalUserId!, userName, "Linked on first sign-in");
                await context.SaveChangesAsync();
            }

            return user;
        }

        /// <summary>
        /// SWA's user id changes when the identity provider does (e.g. moving to a custom Entra ID provider), so a
        /// managed user's existing <see cref="User"/> is found by email and moved to the new id, along with the rows
        /// that store the id directly. Null when there's no single existing user to move.
        /// </summary>
        private async Task<User?> RelinkAsync(ManagedUser managedUser, string email, string externalUserId)
        {
            var candidates = managedUser.UserId is int linkedUserId
                ? await context.Users.Where(u => u.UserId == linkedUserId).ToListAsync()
                : await context.Users
                    .Where(u => u.IdentityProvider == IdentityProviderAad && (u.Email == email || u.UserName.ToLower() == email))
                    .Take(2)
                    .ToListAsync();

            if (candidates.Count != 1)
            {
                return null;
            }

            var user = candidates[0];
            if (!string.Equals(user.IdentityProvider, IdentityProviderAad, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var previousId = user.ExternalUserId;

            // Loaded and changed one by one (rather than ExecuteUpdate) so it runs in the same transaction as the user
            foreach (var budget in await context.Budgets.Where(b => b.UserId == previousId).ToListAsync())
            {
                budget.UserId = externalUserId;
            }
            foreach (var bill in await context.BillTrackings.Where(b => b.UserId == previousId).ToListAsync())
            {
                bill.UserId = externalUserId;
            }
            foreach (var token in await context.OAuthTokens.Where(t => t.UserId == previousId && t.OAuthProvider == OAuthProviders.GitHub).ToListAsync())
            {
                token.UserId = externalUserId;
            }

            user.ExternalUserId = externalUserId;
            user.Email ??= email;
            managedUser.UserId = user.UserId;
            MarkAccepted(managedUser);

            Audit(UserAuditAction.Relinked, managedUser, externalUserId, user.UserName, "Sign-in id changed; existing user and their data moved to it");
            await context.SaveChangesAsync();

            logger.LogInformation("Relinked user {UserId} to a new sign-in id for managed user {ManagedUserId}", user.UserId, managedUser.ManagedUserId);
            return user;
        }

        // Signing in through Entra means a guest has redeemed their invitation
        private static void MarkAccepted(ManagedUser managedUser)
        {
            if (managedUser.InvitationStatus == InvitationStatus.PendingAcceptance)
            {
                managedUser.InvitationStatus = InvitationStatus.Accepted;
            }
        }

        private void Audit(string action, ManagedUser target, string actorUserId, string? actorName, string details)
        {
            context.UserAuditLogs.Add(new UserAuditLog
            {
                ActorUserId = actorUserId,
                ActorName = actorName,
                Action = action,
                TargetManagedUserId = target.ManagedUserId,
                TargetEntraObjectId = target.EntraObjectId,
                TargetEmail = target.Email,
                Details = details,
                OccurredAt = timeProvider.GetUtcNow()
            });
        }
    }
}
