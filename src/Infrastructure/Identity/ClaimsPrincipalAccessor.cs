using Elysian.Application.Interfaces;
using System.Security.Claims;

namespace Elysian.Infrastructure.Identity
{
    public class ClaimsPrincipalAccessor : IClaimsPrincipalAccessor
    {
        private readonly AsyncLocal<ContextHolder> _context = new();

        public ClaimsPrincipal? Principal
        {
            get => _context.Value?.Context;
            set
            {
                var holder = _context.Value;
                if (holder is not null)
                {
                    holder.Context = null;
                }

                if (value is not null)
                {
                    _context.Value = new ContextHolder { Context = value };
                }
            }
        }

        public string? UserId => Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString();
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
        
        public Dictionary<string, string> Claims => Principal?.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => string.Join(",", g.Select(c => c.Value)))
            ?? [];

        private class ContextHolder
        {
            public ClaimsPrincipal? Context;
        }
    }
}
