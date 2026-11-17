using Finbuckle.MultiTenant.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Data
{
    public record ElysianTenantInfo : ITenantInfo
    {
        public string? Id { get; set; }
        public string? Identifier { get; set; }
        public string? Name { get; set; }
        public string? CmsUrl { get; set; }

        /// <summary>
        /// The product line the tenant belongs to, one of <see cref="Platforms.PlatformNames"/>
        /// </summary>
        public string? Platform { get; set; }

        /// <summary>
        /// Which code renders the tenant's public site, one of <see cref="Platforms.RuntimeNames"/>
        /// </summary>
        public string? Runtime { get; set; }

        /// <summary>
        /// One of <see cref="Platforms.TenantStatusNames"/>
        /// </summary>
        public string? Status { get; set; }
    }
}
