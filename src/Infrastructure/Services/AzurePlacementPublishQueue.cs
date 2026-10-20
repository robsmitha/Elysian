using Elysian.Application.Features.Placements;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    public class AzurePlacementPublishQueue(PlacementStorageClients clients, IOptions<PlacementPublishingSettings> settings,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor, ILogger<AzurePlacementPublishQueue> logger)
        : IPlacementPublishQueue
    {
        public async Task EnqueueAsync(string reason, bool force = false)
        {
            if (!settings.Value.Enabled)
            {
                return;
            }

            try
            {
                var tenant = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
                await SendAsync(new PlacementPublishMessage(tenant, reason, force));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not queue a placements publish ({Reason})", reason);
            }
        }

        public async Task SendAsync(PlacementPublishMessage message, TimeSpan? delay = null, CancellationToken cancellationToken = default) =>
            await clients.Queue.SendMessageAsync(PlacementQueue.Serialize(message), visibilityTimeout: delay, cancellationToken: cancellationToken);
    }
}
