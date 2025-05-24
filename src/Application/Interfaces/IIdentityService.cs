using Elysian.Domain.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Interfaces
{
    public interface IIdentityService
    {
        Task<bool> IsInRoleAsync(User user, string roleName);
        Task<bool> AuthorizeAsync(User user, string policyName);
        Task<User> GetOrCreateAsync(ClaimsPrincipal principal);
    }
}
