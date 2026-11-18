using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Elysian.Application.Features.Members.Queries
{
    [Authorize(Policy = PolicyNames.MemberRead)]
    public record GetMembersQuery : IRequest<List<MemberModel>>;

    public class GetMembersQueryHandler(ElysianContext context) : IRequestHandler<GetMembersQuery, List<MemberModel>>
    {
        public async Task<List<MemberModel>> Handle(GetMembersQuery request, CancellationToken cancellationToken) =>
            (await context.TenantMemberships.AsNoTracking().OrderBy(m => m.Email).ToListAsync(cancellationToken))
                .Select(m => new MemberModel(m.TenantMembershipId, m.Email, m.Role, m.Subject != null))
                .ToList();
    }

    [Authorize(Policy = PolicyNames.MemberRead)]
    public record GetInvitesQuery : IRequest<List<InviteModel>>;

    public class GetInvitesQueryHandler(ElysianContext context, TimeProvider timeProvider) : IRequestHandler<GetInvitesQuery, List<InviteModel>>
    {
        public async Task<List<InviteModel>> Handle(GetInvitesQuery request, CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();
            return (await context.TenantInvites.AsNoTracking().OrderByDescending(i => i.TenantInviteId).ToListAsync(cancellationToken))
                .Select(i => new InviteModel(i.TenantInviteId, i.Role, i.Email, i.ExpiresAt,
                    i.RedeemedAt != null ? "Redeemed" : i.RevokedAt != null ? "Revoked" : i.ExpiresAt <= now ? "Expired" : "Pending"))
                .ToList();
        }
    }

    /// <summary>
    /// The sites the signed-in person may manage, for the admin app's site picker. Runs outside any tenant.
    /// </summary>
    [RequiresSignIn]
    public record GetMyTenantsQuery : IRequest<List<MyTenantModel>>;

    public class GetMyTenantsQueryHandler(PlatformContext platform, IClaimsPrincipalAccessor claimsPrincipalAccessor,
        IMultiTenantStore<ElysianTenantInfo> tenantStore, IOptions<AuthSettings> options) : IRequestHandler<GetMyTenantsQuery, List<MyTenantModel>>
    {
        public async Task<List<MyTenantModel>> Handle(GetMyTenantsQuery request, CancellationToken cancellationToken)
        {
            var subject = claimsPrincipalAccessor.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new UnauthorizedAccessException();
            }

            var tenants = (await tenantStore.GetAllAsync()).Where(t => t.Id != null && t.Identifier != null).ToList();

            if (options.Value.PlatformAdminSubjects.Contains(subject, StringComparer.OrdinalIgnoreCase))
            {
                return tenants.OrderBy(t => t.Identifier)
                    .Select(t => new MyTenantModel(t.Identifier!, t.Name, t.Platform, t.Runtime, RoleNames.SystemAdmin))
                    .ToList();
            }

            var roles = await platform.TenantMemberships.AsNoTracking()
                .Where(m => m.Subject == subject)
                .ToDictionaryAsync(m => m.TenantId, m => m.Role, cancellationToken);

            return tenants.Where(t => roles.ContainsKey(t.Id!))
                .OrderBy(t => t.Identifier)
                .Select(t => new MyTenantModel(t.Identifier!, t.Name, t.Platform, t.Runtime, roles[t.Id!]))
                .ToList();
        }
    }
}
