using Elysian.Application.Exceptions;
using Elysian.Application.Features.UserManagement;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Members.Commands
{
    /// <summary>
    /// Takes away someone's place in this tenant. Their account with the sign-in provider is untouched.
    /// </summary>
    [Authorize(Policy = PolicyNames.MemberWrite)]
    public record RemoveMemberCommand(int Id) : IRequest;

    public class RemoveMemberCommandHandler(ElysianContext context) : IRequestHandler<RemoveMemberCommand>
    {
        public async Task Handle(RemoveMemberCommand request, CancellationToken cancellationToken)
        {
            var membership = await context.TenantMemberships.FirstOrDefaultAsync(m => m.TenantMembershipId == request.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(TenantMembership), request.Id);

            var isOwner = string.Equals(membership.Role, RoleNames.Owner, StringComparison.OrdinalIgnoreCase);
            if (isOwner && await context.TenantMemberships.CountAsync(m => m.Role == RoleNames.Owner, cancellationToken) <= 1)
            {
                throw new ConflictException("A site needs at least one owner. Make someone else an owner first.");
            }

            membership.IsDeleted = true;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Gives this tenant's existing admins a membership, matched by email on their first sign-in with the new
    /// provider. Run once per tenant before it moves to membership-based roles; running it again adds nothing.
    /// </summary>
    [Authorize(Roles = RoleNames.SystemAdmin)]
    public record BackfillMembershipsCommand : IRequest<int>;

    public class BackfillMembershipsCommandHandler(ElysianContext context) : IRequestHandler<BackfillMembershipsCommand, int>
    {
        private static readonly string[] AdminRoles = [RoleNames.MerchantAdmin, RoleNames.SystemAdmin];

        public async Task<int> Handle(BackfillMembershipsCommand request, CancellationToken cancellationToken)
        {
            var users = await context.Users.AsNoTracking().ToListAsync(cancellationToken);
            var existing = (await context.TenantMemberships.Select(m => m.Email).ToListAsync(cancellationToken))
                .Where(e => e != null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var added = 0;
            foreach (var user in users)
            {
                var isAdmin = user.AccessControl?.Roles?.Any(r => AdminRoles.Contains(r, StringComparer.OrdinalIgnoreCase)) == true;
                var candidate = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;
                if (!isAdmin || !UserManagementService.LooksLikeEmail(candidate))
                {
                    continue;
                }

                var email = UserManagementService.NormalizeEmail(candidate!);
                if (!existing.Add(email))
                {
                    continue;
                }

                context.TenantMemberships.Add(new TenantMembership { Email = email, Role = RoleNames.Owner });
                added++;
            }

            await context.SaveChangesAsync(cancellationToken);
            return added;
        }
    }
}
