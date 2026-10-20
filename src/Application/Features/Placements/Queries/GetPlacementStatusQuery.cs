using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Placements.Queries
{
    /// <summary>
    /// Per slot: what it should show, and whether that's what's published. Comparing against the database (rather
    /// than reading queue depth) means a lost or failed publish still shows as out of date.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record GetPlacementStatusQuery : IRequest<PlacementStatusModel>;

    public class GetPlacementStatusQueryHandler(PlacementResolver resolver, IPlacementStore store,
        IOptions<PlacementPublishingSettings> settings, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetPlacementStatusQuery, PlacementStatusModel>
    {
        public async Task<PlacementStatusModel> Handle(GetPlacementStatusQuery request, CancellationToken cancellationToken)
        {
            if (!settings.Value.Enabled)
            {
                return new PlacementStatusModel(false, []);
            }

            var tenant = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            var resolved = await resolver.ResolveAsync(cancellationToken);

            var slots = new List<PlacementSlotStatus>(resolved.Count);
            foreach (var slot in resolved)
            {
                var published = await store.GetPublishedAsync(PlacementPaths.Variant(tenant, slot.Slot, slot.Slot.LargestWidth), cancellationToken);
                slots.Add(new PlacementSlotStatus(
                    slot.Slot.Key,
                    slot.Slot.Name,
                    slot.Source.ToString(),
                    slot.Photo?.PhotoId,
                    published?.Fingerprint == slot.Fingerprint,
                    published?.PublishedAt,
                    slot.SourceWidth,
                    slot.Slot.LargestWidth));
            }

            return new PlacementStatusModel(true, slots);
        }
    }
}
