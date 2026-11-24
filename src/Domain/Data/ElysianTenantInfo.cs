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

        /// <summary>
        /// The shared template a cloud tenant's site is drawn with, e.g. "classic". Null for a custom site,
        /// which has its own code.
        /// </summary>
        public string? TemplateKey { get; set; }

        /// <summary>
        /// The version of <see cref="TemplateKey"/> the tenant is pinned to. A breaking change to a template is
        /// a new version, and tenants move to it one at a time.
        /// </summary>
        public int? TemplateVersion { get; set; }

        /// <summary>
        /// The published version of the tenant's site content (colours, wording) that the edge serves. Null
        /// until something has been published.
        /// </summary>
        public int? ContentVersion { get; set; }

        /// <summary>
        /// For a site marked as being deleted: when its photos and data are removed. Until then nothing has
        /// been removed and the site can be brought back. Null for every other site.
        /// </summary>
        public DateTimeOffset? DeleteAfter { get; set; }

        /// <summary>
        /// The uploaded photos this site may hold, in gigabytes, when it has a limit of its own. Null for the
        /// platform's default (PlatformStorage:DefaultQuotaGb).
        /// </summary>
        public int? StorageQuotaGb { get; set; }
    }
}
