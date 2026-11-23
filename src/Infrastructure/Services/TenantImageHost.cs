using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Which public address the current tenant's images are served from. A site drawn with a template is served
    /// by the platform's Worker, which loads images from the platform's images host and allows no other; a site
    /// with its own code keeps the host it was built against.
    /// </summary>
    public class TenantImageHost(IOptions<PlatformImagesSettings> settings, IMultiTenantContextAccessor<ElysianTenantInfo>? tenants = null)
    {
        /// <summary>
        /// The public address of a container for the current tenant, e.g. https://images.example.com/photos, or
        /// null when the tenant uses the host's own setting
        /// </summary>
        public string? ContainerUrl(string containerName) =>
            settings.Value.Origin is { } origin && tenants?.MultiTenantContext?.TenantInfo?.Runtime == RuntimeNames.Cloud
                ? $"{origin}/{containerName.Trim('/')}"
                : null;
    }
}
