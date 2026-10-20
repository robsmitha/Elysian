using Elysian.Application.Features.Placements.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Admin changes only enqueue a publish; the host's queue trigger sends PublishPlacementsCommand, which does the
    /// slow part, so admin requests stay fast
    /// </summary>
    public interface IPlacementPublishQueue
    {
        /// <summary>
        /// For the current tenant. Never throws and does nothing when publishing is disabled: the change is already
        /// saved, and the admin status shows any slot that didn't publish as out of date.
        /// </summary>
        Task EnqueueAsync(string reason, bool force = false);

        Task SendAsync(PlacementPublishMessage message, TimeSpan? delay = null, CancellationToken cancellationToken = default);
    }
}
