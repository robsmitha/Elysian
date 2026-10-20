using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Placements.Commands
{
    /// <summary>
    /// Publishes a tenant's reserved placement images, or (with <see cref="PlacementPublishMessage.Repurge"/>) purges
    /// URLs again. Sent by the host's placements-publish queue trigger (no user principal, like ProcessPhotoCommand),
    /// so it carries no [Authorize]; the caller must have set the tenant context first.
    ///
    /// lock → resolve from the database → skip unchanged slots → render → write every file → purge those exact URLs
    /// → confirm through the CDN. Idempotent: it always recomputes from the database, so retries and bursts of
    /// messages converge on the same result.
    /// </summary>
    public record PublishPlacementsCommand(PlacementPublishMessage Message) : IRequest<PlacementPublishResult>;

    public class PublishPlacementsCommandHandler(
        PlacementResolver resolver,
        IPlacementRenderer renderer,
        IPlacementStore store,
        ICdnPurger purger,
        IPlacementPublishQueue queue,
        IPhotoStorage photoStorage,
        IOptions<PlacementPublishingSettings> settings,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        ILogger<PublishPlacementsCommandHandler> logger)
        : IRequestHandler<PublishPlacementsCommand, PlacementPublishResult>
    {
        /// <summary>
        /// Lock held by another publish: try again shortly rather than run two at once
        /// </summary>
        private static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Comfortably longer than a normal publish, so it lands after the writes and the regular purge
        /// </summary>
        private static readonly TimeSpan SafetyNetDelay = TimeSpan.FromMinutes(5);

        public async Task<PlacementPublishResult> Handle(PublishPlacementsCommand request, CancellationToken cancellationToken)
        {
            if (!settings.Value.Enabled)
            {
                return new PlacementPublishResult(PlacementPublishOutcome.Disabled);
            }

            if (request.Message.Repurge is { Length: > 0 } repurge)
            {
                await purger.PurgeAsync(repurge, cancellationToken);
                return new PlacementPublishResult(PlacementPublishOutcome.Repurged, PurgedUrls: repurge.Length);
            }

            var tenant = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            await using var publishLock = await store.TryAcquireLockAsync(PlacementPaths.Lock(tenant), cancellationToken);
            if (publishLock == null)
            {
                await queue.SendAsync(request.Message, BusyRetryDelay, cancellationToken);
                return new PlacementPublishResult(PlacementPublishOutcome.Busy);
            }

            var resolved = await resolver.ResolveAsync(cancellationToken);

            var changed = new List<ResolvedSlot>();
            foreach (var slot in resolved)
            {
                var published = await store.GetPublishedAsync(PlacementPaths.Variant(tenant, slot.Slot, slot.Slot.LargestWidth), cancellationToken);
                if (request.Message.Force || published?.Fingerprint != slot.Fingerprint)
                {
                    changed.Add(slot);
                }
            }

            if (changed.Count == 0)
            {
                return new PlacementPublishResult(PlacementPublishOutcome.UpToDate);
            }

            // 1. Safety net before writing anything: if this run dies partway (e.g. out of memory on a huge
            //    original), a retry skips the slots already written because their fingerprints match, so nothing
            //    else would purge them. The URLs are deterministic, so they're known up front.
            if (purger.IsConfigured)
            {
                var planned = changed
                    .SelectMany(s => s.Slot.Widths.Select(w => store.GetPublicUrl(PlacementPaths.Variant(tenant, s.Slot, w))))
                    .ToArray();
                await queue.SendAsync(new PlacementPublishMessage(tenant, "repurge-safety-net", Repurge: planned),
                    SafetyNetDelay, cancellationToken);
            }

            // 2. Write every changed file before purging anything, so the CDN never drops the old set while the new
            //    one is half written
            var written = new List<PublishedFile>();
            foreach (var group in changed.GroupBy(s => s.Photo?.PhotoId))
            {
                var photo = group.First().Photo;
                if (photo == null)
                {
                    foreach (var slot in group)
                    {
                        var variants = await renderer.RenderNeutralAsync(slot.Slot, cancellationToken);
                        written.AddRange(await UploadAsync(tenant, slot, variants, cancellationToken));
                    }
                }
                else
                {
                    // One decode per original, however many slots it fills
                    await using var original = await photoStorage.OpenOriginalAsync(photo.OriginalBlobName, cancellationToken);
                    using var source = await renderer.LoadAsync(original, cancellationToken);

                    foreach (var slot in group)
                    {
                        var variants = await source.RenderAsync(slot.Slot, photo.FocusX, photo.FocusY, cancellationToken);
                        written.AddRange(await UploadAsync(tenant, slot, variants, cancellationToken));
                    }
                }

                // Big originals take a while; keep the short lock alive between them
                await publishLock.RenewAsync(cancellationToken);
            }

            var urls = written.Select(w => w.Url).ToList();

            if (purger.IsConfigured)
            {
                // 3. Queue the second purge BEFORE the first. It catches a miss that fetched old bytes just before the
                //    overwrite, and it's the durable record that these URLs need purging: if the purge below fails, a
                //    retry of this message finds the fingerprints current and skips, so this message gets them purged
                await queue.SendAsync(new PlacementPublishMessage(tenant, "repurge", Repurge: [.. urls]),
                    TimeSpan.FromSeconds(settings.Value.RepurgeDelaySeconds), cancellationToken);

                // 4. Purge exactly what was written, every width
                await purger.PurgeAsync(urls, cancellationToken);

                // 5. Check the nearest CDN location now serves the new bytes
                if (settings.Value.ConfirmAfterPurge)
                {
                    await ConfirmAsync(written, cancellationToken);
                }
            }

            logger.LogInformation("Published {Slots} placement slots ({Files} files): {Names}",
                changed.Count, urls.Count, string.Join(", ", changed.Select(c => $"{c.Slot.Name}={c.Source}")));

            return new PlacementPublishResult(PlacementPublishOutcome.Published, changed.Count, purger.IsConfigured ? urls.Count : 0);
        }

        /// <summary>
        /// Smallest first (the order the renderer returns), so the largest, which carries the fingerprint the next
        /// publish compares against, lands last
        /// </summary>
        private async Task<List<PublishedFile>> UploadAsync(string tenant, ResolvedSlot slot,
            IReadOnlyList<PlacementVariant> variants, CancellationToken cancellationToken)
        {
            var metadata = new PlacementMetadata(slot.Fingerprint, slot.Source, slot.Photo?.PhotoId,
                slot.SourceWidth ?? 0, DateTimeOffset.UtcNow);

            if (slot.SourceWidth < slot.Slot.LargestWidth)
            {
                logger.LogWarning("Placement {Slot}: source crop is {SourceWidth}px wide, below the {Largest}px variant",
                    slot.Slot.Name, slot.SourceWidth, slot.Slot.LargestWidth);
            }

            var written = new List<PublishedFile>(variants.Count);
            foreach (var variant in variants.OrderBy(v => v.NamedWidth))
            {
                var path = PlacementPaths.Variant(tenant, slot.Slot, variant.NamedWidth);
                var etag = await store.UploadAsync(path, variant.Content, metadata, cancellationToken);
                written.Add(new PublishedFile(store.GetPublicUrl(path), etag));
            }

            return written;
        }

        /// <summary>
        /// One extra purge for anything still stale; if it still is, log an error (alert on it) rather than throw,
        /// because a retry would find the fingerprints current and skip, while the queued second purge still runs.
        /// This checks one CDN location; the purge itself is global.
        /// </summary>
        private async Task ConfirmAsync(IReadOnlyList<PublishedFile> written, CancellationToken cancellationToken)
        {
            var stale = await purger.FindStaleAsync(written, cancellationToken);
            if (stale.Count == 0)
            {
                return;
            }

            logger.LogWarning("{Count} placement URLs still served old bytes after purge; purging again", stale.Count);
            await purger.PurgeAsync(stale.Select(s => s.Url).ToList(), cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            var stillStale = await purger.FindStaleAsync(stale, cancellationToken);
            if (stillStale.Count > 0)
            {
                logger.LogError("Placement purge not confirmed: the CDN still serves old bytes for {Urls}",
                    string.Join(", ", stillStale.Select(s => s.Url)));
            }
        }
    }
}
