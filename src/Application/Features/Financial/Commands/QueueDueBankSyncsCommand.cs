using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>
    /// The nightly catch-up for the current tenant: queues a sync for each bank that hasn't synced lately,
    /// so anything a webhook missed is picked up. Run as a tenant job, so it is only ever queued for tenants
    /// with the finance module. Each bank then syncs on its own from the plaid-sync queue.
    /// </summary>
    /// <returns>What was done, in a line for the log</returns>
    public record QueueDueBankSyncsCommand : IRequest<string>;

    public class QueueDueBankSyncsCommandHandler(ITransactionSyncService sync, IPlaidSyncQueue queue,
        ILogger<QueueDueBankSyncsCommandHandler> logger) : IRequestHandler<QueueDueBankSyncsCommand, string>
    {
        /// <summary>Banks synced within this long are left alone</summary>
        public static readonly TimeSpan Freshness = TimeSpan.FromHours(20);

        public async Task<string> Handle(QueueDueBankSyncsCommand request, CancellationToken cancellationToken)
        {
            var due = await sync.GetItemsDueForSyncAsync(Freshness, cancellationToken);
            foreach (var itemId in due)
            {
                await queue.EnqueueAsync(itemId, SyncTrigger.Nightly, cancellationToken);
            }

            if (due.Count > 0)
            {
                logger.LogInformation("Queued {Count} nightly bank sync(s)", due.Count);
            }
            return $"Queued {due.Count} bank sync(s)";
        }
    }
}
