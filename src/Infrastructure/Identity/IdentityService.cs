using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

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

            var policies = GetPolicies(roles);

            user = new User
            {
                ExternalUserId = externalUserId,
                UserName = userName,
                IdentityProvider = identityProvider,
                AccessControl = new AccessControl
                {
                    Roles = roles,
                    Policies = policies
                }
            };

            await context.AddAsync(user);
            await context.SaveChangesAsync();

            return user;
        }

        private static List<string> GetPolicies(List<string> roles)
        {
            var roleHierarchy = new Dictionary<string, (int Level, string[] Policies)>
            {
                ["systemAdmin"] = (3, new[] { "user:read", "user:write" }),
                ["merchantAdmin"] = (2, new[] { "product:read", "product:write" })
            };

            var userLevel = roles
                .Where(role => roleHierarchy.ContainsKey(role))
                .Select(role => roleHierarchy[role].Level)
                .DefaultIfEmpty(0)
                .Max();

            var policies = roleHierarchy
                .Where(kv => kv.Value.Level <= userLevel)
                .SelectMany(kv => kv.Value.Policies)
                .Distinct()
                .ToList();

            return policies;
        }
    }
}
