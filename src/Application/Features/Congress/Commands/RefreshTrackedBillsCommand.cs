using System.Collections.Concurrent;
using CapitolSharp.Congress;
using CapitolSharp.Congress.Bills;
using CapitolSharp.Congress.Congresses;
using CapitolSharp.Congress.Enums;
using Elysian.Application.Features.Congress.Models;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Bill = CapitolSharp.Congress.Bills.BillDetails.Bill;

namespace Elysian.Application.Features.Congress.Commands
{
    /// <summary>
    /// Keeps the current tenant's saved bills' snapshots current, which is what flags new activity for their
    /// readers. Run as a tenant job, so it is only ever queued for tenants with the congress module.
    /// One bill that can't be fetched is logged and tried again next run; it never stops the rest.
    /// </summary>
    /// <returns>What was done, in a line for the log</returns>
    public record RefreshTrackedBillsCommand : IRequest<string>;

    public class RefreshTrackedBillsCommandHandler(ElysianContext context, CapitolSharpCongress congressClient,
        TimeProvider timeProvider, ILogger<RefreshTrackedBillsCommandHandler> logger) : IRequestHandler<RefreshTrackedBillsCommand, string>
    {
        // Congress.gov allows 5,000 requests an hour per key; a few at a time stays well clear of it.
        private const int MaxConcurrentRequests = 4;

        public async Task<string> Handle(RefreshTrackedBillsCommand request, CancellationToken cancellationToken)
        {
            // Nothing saved: no reason to ask Congress.gov anything
            if (!await context.BillTrackings.AnyAsync(cancellationToken))
            {
                return "No saved bills";
            }

            // Bills from past Congresses can't change any more, so only the current one needs checking.
            var currentCongress = await congressClient.SendAsync(new CongressCurrentListRequest());
            if (currentCongress?.Congress?.Number is not int congress)
            {
                logger.LogWarning("Skipped refreshing saved bills: could not get the current Congress.");
                return "Skipped: could not get the current Congress";
            }

            var billTrackings = await context.BillTrackings
                .Where(b => b.Congress == congress)
                .ToListAsync(cancellationToken);

            if (billTrackings.Count == 0)
            {
                return "No saved bills in the current Congress";
            }

            // Many readers can save the same bill; ask Congress.gov about each one once.
            var bills = new ConcurrentDictionary<(string BillType, int BillNumber), Bill>();
            var billKeys = billTrackings.Select(b => (b.BillType, b.BillNumber)).Distinct();

            await Parallel.ForEachAsync(billKeys,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentRequests, CancellationToken = cancellationToken },
                async (key, _) =>
                {
                    try
                    {
                        var response = await congressClient.SendAsync(new BillDetailsRequest
                        {
                            Congress = congress,
                            BillType = Enum.Parse<BillType>(key.BillType, ignoreCase: true),
                            BillNumber = key.BillNumber
                        });

                        if (response?.Bill != null)
                        {
                            bills[key] = response.Bill;
                        }
                    }
                    catch (Exception ex)
                    {
                        // One bad bill shouldn't stop the rest; it gets another try next run.
                        logger.LogWarning(ex, "Could not refresh saved bill {Congress} {BillType} {BillNumber}.",
                            congress, key.BillType, key.BillNumber);
                    }
                });

            var refreshedAt = timeProvider.GetUtcNow();
            var newActivity = 0;
            var refreshed = 0;
            foreach (var billTracking in billTrackings)
            {
                if (bills.TryGetValue((billTracking.BillType, billTracking.BillNumber), out var bill))
                {
                    var hadUnseenActivity = billTracking.HasUnseenActivity;
                    billTracking.ApplySnapshot(bill, refreshedAt);
                    refreshed++;
                    if (billTracking.HasUnseenActivity && !hadUnseenActivity)
                    {
                        newActivity++;
                    }
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Refreshed {Refreshed} of {Total} saved bills; {NewActivity} have new activity.",
                refreshed, billTrackings.Count, newActivity);
            return $"Refreshed {refreshed} of {billTrackings.Count} saved bills; {newActivity} with new activity";
        }
    }
}
