using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Interfaces
{
    public interface IClaimsPrincipalAccessor
    {
        ClaimsPrincipal? Principal { get; set; }
        string? UserId { get; }
        bool IsAuthenticated { get; }
    }
}
