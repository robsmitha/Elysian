using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Platform.Commands
{
    /// <summary>
    /// Deletes a site for good. Two deliberate steps come first: the site must already be suspended, and its
    /// name must be typed again. Only a site drawn with a shared template can be deleted this way; a site with
    /// its own code has a repository, a Worker and a domain that nothing here knows how to take down.
    ///
    /// This stops the site answering at once and marks it as being deleted. Nothing is removed yet: its photos
    /// and data are kept for a number of days (<see cref="PlatformDeletionSettings.GraceDays"/>), and until
    /// they run out <see cref="RestoreDeletedTenantCommand"/> brings the site back. After that the rest (its
    /// files, then its rows) is done by a queued job, <see cref="PurgeSiteCommand"/>, because a large photo
    /// library takes longer than a request may. <see cref="EnqueueDueSiteDeletionsCommand"/>, sent daily,
    /// queues that job for every site whose days have run out, which is also how a removal that failed
    /// partway is finished.
    /// </summary>
    /// <param name="Confirm">The site's identifier, typed again</param>
    /// <param name="Now">Remove everything straight away, with no days to change one's mind. For a test site.</param>
    [RequiresPlatformAdmin]
    public record DeleteTenantCommand(string? Identifier, string? Confirm, bool Now = false) : IRequest;

    public class DeleteTenantCommandValidator : AbstractValidator<DeleteTenantCommand>
    {
        public DeleteTenantCommandValidator()
        {
            RuleFor(v => v.Identifier).NotEmpty().WithMessage("Choose a site.");
            RuleFor(v => v.Confirm)
                .Must((command, confirm) => !string.IsNullOrWhiteSpace(confirm)
                    && TenantIdentifiers.Normalize(confirm) == TenantIdentifiers.Normalize(command.Identifier))
                .WithMessage("Type the site's name exactly to confirm.");
        }
    }

    public class DeleteTenantCommandHandler(PlatformContext platform, ITenantJobQueue queue, IEdgeHostMap? hostMap = null,
        ILogger<DeleteTenantCommandHandler>? logger = null, IOptions<PlatformDeletionSettings>? settings = null,
        TimeProvider? timeProvider = null) : IRequestHandler<DeleteTenantCommand>
    {
        private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

        public async Task Handle(DeleteTenantCommand request, CancellationToken cancellationToken)
        {
            var identifier = TenantIdentifiers.Normalize(request.Identifier);
            var tenant = await platform.Tenants.FirstOrDefaultAsync(t => t.Identifier == identifier, cancellationToken)
                ?? throw new NotFoundException("Site", identifier);

            if (tenant.Runtime != RuntimeNames.Cloud)
            {
                throw new ConflictException("This site has its own code, so it can't be deleted here.");
            }
            if (tenant.Status != TenantStatusNames.Suspended && tenant.Status != TenantStatusNames.Deleting)
            {
                throw new ConflictException("Suspend the site first. Only a suspended site can be deleted.");
            }

            var now = time.GetUtcNow();
            var days = (settings?.Value ?? new PlatformDeletionSettings()).Days;
            if (request.Now || days == 0)
            {
                tenant.DeleteAfter = now;
            }
            else if (tenant.Status != TenantStatusNames.Deleting)
            {
                tenant.DeleteAfter = now.AddDays(days);
            }
            // Asked again for a site already being deleted: its days are not started over. A site marked
            // before there were any days to wait has no date, and is due.
            tenant.Status = TenantStatusNames.Deleting;
            await platform.SaveChangesAsync(cancellationToken);
            var due = tenant.DeleteAfter == null || tenant.DeleteAfter <= now;

            // Its addresses stop answering now, not when the job gets to them
            if (hostMap != null)
            {
                var hosts = await platform.TenantDomains.AsNoTracking().Where(d => d.TenantId == tenant.Id).Select(d => d.Host).ToListAsync(cancellationToken);
                foreach (var host in hosts)
                {
                    await hostMap.RemoveAsync(host, cancellationToken);
                }
            }

            if (due)
            {
                await queue.EnqueueAsync(new TenantJobMessage(identifier, TenantJobCatalog.DeleteSite), cancellationToken);
                logger?.LogWarning("{Tenant} is being deleted", identifier);
            }
            else
            {
                logger?.LogWarning("{Tenant} is marked as being deleted; its photos and data are removed after {DeleteAfter:u}", identifier, tenant.DeleteAfter);
            }
        }
    }

    /// <summary>
    /// Brings back a site that was deleted, while its days to change one's mind last. Nothing of it has been
    /// removed in that time, so it comes back whole: suspended, as it was before it was deleted, and restored
    /// from there like any suspended site. Once the days have run out its photos and data are being removed
    /// and it can't be brought back.
    /// </summary>
    [RequiresPlatformAdmin]
    public record RestoreDeletedTenantCommand(string? Identifier) : IRequest;

    public class RestoreDeletedTenantCommandHandler(PlatformContext platform, IEdgeHostMap? hostMap = null,
        ILogger<RestoreDeletedTenantCommandHandler>? logger = null, TimeProvider? timeProvider = null) : IRequestHandler<RestoreDeletedTenantCommand>
    {
        private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

        public async Task Handle(RestoreDeletedTenantCommand request, CancellationToken cancellationToken)
        {
            var identifier = TenantIdentifiers.Normalize(request.Identifier);
            var tenant = await platform.Tenants.FirstOrDefaultAsync(t => t.Identifier == identifier, cancellationToken)
                ?? throw new NotFoundException("Site", identifier);

            if (tenant.Status != TenantStatusNames.Deleting)
            {
                throw new ConflictException("This site is not being deleted.");
            }
            if (tenant.DeleteAfter == null || tenant.DeleteAfter <= time.GetUtcNow())
            {
                throw new ConflictException("It is too late to bring this site back: its photos and data are being removed.");
            }

            tenant.Status = TenantStatusNames.Suspended;
            tenant.DeleteAfter = null;
            await platform.SaveChangesAsync(cancellationToken);

            // Its addresses are known at the edge again, as a suspended site's
            if (hostMap != null)
            {
                await hostMap.SyncAsync(identifier, cancellationToken);
            }
            logger?.LogWarning("{Tenant} was brought back from being deleted", identifier);
        }
    }

    /// <summary>
    /// Queues the removal of every site whose days to change one's mind have run out. Sent by a daily timer,
    /// outside any tenant. A site whose removal failed partway is still marked and still due, so it is queued
    /// again each day until it is gone.
    /// </summary>
    /// <returns>How many were queued</returns>
    public record EnqueueDueSiteDeletionsCommand : IRequest<int>;

    public class EnqueueDueSiteDeletionsCommandHandler(PlatformContext platform, ITenantJobQueue queue,
        ILogger<EnqueueDueSiteDeletionsCommandHandler> logger, TimeProvider? timeProvider = null) : IRequestHandler<EnqueueDueSiteDeletionsCommand, int>
    {
        private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

        public async Task<int> Handle(EnqueueDueSiteDeletionsCommand request, CancellationToken cancellationToken)
        {
            var now = time.GetUtcNow();
            var due = (await platform.Tenants.AsNoTracking()
                    .Where(t => t.Status == TenantStatusNames.Deleting && t.Runtime == RuntimeNames.Cloud)
                    .ToListAsync(cancellationToken))
                .Where(t => !string.IsNullOrEmpty(t.Identifier) && (t.DeleteAfter == null || t.DeleteAfter <= now))
                .ToList();

            var queued = 0;
            foreach (var tenant in due)
            {
                try
                {
                    await queue.EnqueueAsync(new TenantJobMessage(tenant.Identifier!, TenantJobCatalog.DeleteSite), cancellationToken);
                    queued++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // Keep going: tomorrow's run picks up any that couldn't be queued
                    logger.LogError(ex, "Couldn't queue the removal of {Tenant}", tenant.Identifier);
                }
            }

            if (due.Count > 0)
            {
                logger.LogWarning("Queued the removal of {Queued} of {Due} site(s) due to be deleted", queued, due.Count);
            }
            return queued;
        }
    }

    /// <summary>
    /// The queued half of deleting a site, run inside it: what is cached at the edge, its files, then every
    /// row it has. It refuses any site that hasn't been marked as being deleted, or whose days to change
    /// one's mind haven't run out, so no stray message can remove a live one or one that can still be
    /// brought back. Each step removes what is there and is safe to run again; a failure leaves the site
    /// marked as being deleted, and deleting it again from the admin app finishes the job.
    ///
    /// Order: the edge first (nothing is served), then files (nothing is downloadable), then rows, the tenant's
    /// own last. If rows went first, nothing would be left to say whose the files were.
    /// </summary>
    public record PurgeSiteCommand : IRequest<string>;

    public class PurgeSiteCommandHandler(ElysianContext context, PlatformContext platform, ITenantDataEraser eraser,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor, ILogger<PurgeSiteCommandHandler> logger,
        IEdgeConfigStore? edge = null, IEdgeHostMap? hostMap = null, IPhotoStorage? photos = null, IPlacementStore? placements = null,
        ICdnPurger? purger = null, TimeProvider? timeProvider = null) : IRequestHandler<PurgeSiteCommand, string>
    {
        public const string NotMarked = "Skipped: the site is not marked as being deleted";
        public const string NotDue = "Skipped: the site can still be brought back";

        public async Task<string> Handle(PurgeSiteCommand request, CancellationToken cancellationToken)
        {
            var current = multiTenantContextAccessor.MultiTenantContext.TenantInfo!;

            // Read afresh: what the request was resolved with may be a moment old
            var tenant = await platform.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == current.Id, cancellationToken);
            if (tenant == null)
            {
                return "Already gone";
            }
            if (tenant.Status != TenantStatusNames.Deleting || tenant.Runtime != RuntimeNames.Cloud)
            {
                logger.LogError("Refused to purge {Tenant}: it is {Status}, not marked as being deleted", tenant.Identifier, tenant.Status);
                return NotMarked;
            }

            if (tenant.DeleteAfter > (timeProvider ?? TimeProvider.System).GetUtcNow())
            {
                logger.LogError("Refused to purge {Tenant}: it can be brought back until {DeleteAfter:u}", tenant.Identifier, tenant.DeleteAfter);
                return NotDue;
            }

            var identifier = tenant.Identifier!;
            // The slash matters: "friend/" is one site's folder, "friend" would also match "friend-two/"
            var folder = $"{identifier}/";

            // 1. The edge: its addresses again (one may have been added since), and every published version
            if (hostMap != null)
            {
                foreach (var host in await platform.TenantDomains.AsNoTracking().Where(d => d.TenantId == tenant.Id).Select(d => d.Host).ToListAsync(cancellationToken))
                {
                    await hostMap.RemoveAsync(host, cancellationToken);
                }
            }
            if (edge is { IsConfigured: true })
            {
                foreach (var version in await context.SiteContentVersions.AsNoTracking().Select(v => v.Version).ToListAsync(cancellationToken))
                {
                    await edge.DeleteAsync(EdgeKeys.Content(identifier, version), cancellationToken);
                }
            }

            // 2. Files. Their public addresses are noted first, so the copies the CDN holds can be purged:
            //    a gallery photo is cached there for a year.
            var urls = new List<string>();
            var files = 0;
            if (photos != null)
            {
                var variants = await photos.ListVariantsAsync(folder, cancellationToken);
                urls.AddRange(variants.Select(photos.GetPublicUrl));
                await photos.DeleteVariantsAsync(folder, cancellationToken);
                files += variants.Count;
                files += await photos.DeleteOriginalsAsync(folder, cancellationToken);
            }
            if (placements != null)
            {
                var removed = await placements.DeleteAllAsync(folder, cancellationToken);
                urls.AddRange(removed.Select(placements.GetPublicUrl));
                files += removed.Count;
            }
            if (purger is { IsConfigured: true } && urls.Count > 0)
            {
                try
                {
                    await purger.PurgeAsync(urls, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The files are gone from storage either way; a cached copy lasts only until the CDN drops it
                    logger.LogError(ex, "Deleted {Tenant}'s files, but could not purge {Count} of their addresses from the CDN", identifier, urls.Count);
                }
            }

            // 3. Rows, the tenant's own last
            var rows = await eraser.EraseAsync(tenant.Id!, identifier, cancellationToken);

            logger.LogWarning("Deleted {Tenant}: {Files} files and {Rows} rows", identifier, files, rows);
            return $"Deleted {identifier}: {files} files, {rows} rows";
        }
    }
}
