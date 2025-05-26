using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Elysian.Infrastructure.Identity
{
    public class IdentityService(ElysianContext context, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor) : IIdentityService
    {
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
            var roles = principal?.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList() ?? [];

            user = new User
            {
                ExternalUserId = externalUserId,
                UserName = userName,
                IdentityProvider = identityProvider,
                AccessControl = new AccessControl
                {
                    Roles = roles,
                    Policies = PolicyNames.GetDefaultPolicies(roles)
                }
            };

            await context.AddAsync(user);
            await context.SaveChangesAsync();

            return user;
        }
    }
}
