using Elysian.Application.Features.Platform;

namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Queues scheduled work for one tenant (a Storage queue the Function App drains)
    /// </summary>
    public interface ITenantJobQueue
    {
        Task EnqueueAsync(TenantJobMessage message, CancellationToken cancellationToken = default);
    }
}
