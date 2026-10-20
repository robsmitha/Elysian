using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Placements.Commands
{
    /// <summary>
    /// Queues a rebuild of every slot regardless of fingerprints: the initial backfill, and recovery.
    /// False when publishing is disabled.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record RepublishPlacementsCommand : IRequest<bool>;

    public class RepublishPlacementsCommandHandler(IPlacementPublishQueue queue, IOptions<PlacementPublishingSettings> settings,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<RepublishPlacementsCommand, bool>
    {
        public async Task<bool> Handle(RepublishPlacementsCommand request, CancellationToken cancellationToken)
        {
            if (!settings.Value.Enabled)
            {
                return false;
            }

            // Sent directly (not EnqueueAsync) so a queue failure reaches the admin instead of being logged away
            var tenant = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            await queue.SendAsync(new PlacementPublishMessage(tenant, "republish", Force: true), cancellationToken: cancellationToken);
            return true;
        }
    }
}
