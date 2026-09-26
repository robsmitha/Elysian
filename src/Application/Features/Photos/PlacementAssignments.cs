using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Photos
{
    /// <summary>
    /// Writes to <see cref="Placement"/> rows. Changes are tracked, not saved; callers save once.
    /// A spot always shows exactly one photo, so assigning a spot that another photo fills moves it.
    /// </summary>
    public static class PlacementAssignments
    {
        /// <summary>
        /// Makes <paramref name="keys"/> the complete set of spots this photo fills
        /// </summary>
        public static async Task SetSpotsForPhotoAsync(this ElysianContext context, Guid photoId,
            IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            var wanted = keys.ToHashSet(StringComparer.Ordinal);

            var affected = await context.Placements
                .Where(p => p.PhotoId == photoId || wanted.Contains(p.Key))
                .ToListAsync(cancellationToken);

            foreach (var placement in affected)
            {
                if (wanted.Remove(placement.Key))
                {
                    placement.PhotoId = photoId;
                }
                else
                {
                    // One of this photo's spots that it no longer fills
                    placement.IsDeleted = true;
                }
            }

            foreach (var key in wanted)
            {
                context.Placements.Add(new Placement { Key = key, PhotoId = photoId });
            }
        }

        /// <summary>
        /// Points one spot at a photo, or clears it when <paramref name="photoId"/> is null
        /// </summary>
        public static async Task SetSpotAsync(this ElysianContext context, string key, Guid? photoId,
            CancellationToken cancellationToken)
        {
            var placement = await context.Placements.SingleOrDefaultAsync(p => p.Key == key, cancellationToken);

            if (photoId == null)
            {
                if (placement != null)
                {
                    placement.IsDeleted = true;
                }
                return;
            }

            if (placement == null)
            {
                context.Placements.Add(new Placement { Key = key, PhotoId = photoId.Value });
            }
            else
            {
                placement.PhotoId = photoId.Value;
            }
        }

        public static async Task ClearSpotsForPhotoAsync(this ElysianContext context, Guid photoId,
            CancellationToken cancellationToken)
        {
            var placements = await context.Placements.Where(p => p.PhotoId == photoId).ToListAsync(cancellationToken);
            placements.ForEach(p => p.IsDeleted = true);
        }
    }
}
