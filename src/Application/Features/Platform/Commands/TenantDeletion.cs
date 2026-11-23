using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Services;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Platform.Commands
{
    /// <summary>
    /// Deletes a site for good. Two deliberate steps come first: the site must already be suspended, and its
    /// name must be typed again. Only a site drawn with a shared template can be deleted this way; a site with
    /// its own code has a repository, a Worker and a domain that nothing here knows how to take down.
    ///
    /// This stops the site answering at once and marks it as being deleted. The rest (its files, then its
    /// rows) is done by a queued job, <see cref="PurgeSiteCommand"/>, because a large photo library takes
    /// longer than a request may. Sending this again for a site already being deleted queues that job again,
    /// which is how a delete that failed partway is finished.
    /// </summary>
    /// <param name="Confirm">The site's identifier, typed again</param>
    [RequiresPlatformAdmin]
    public record DeleteTenantCommand(string? Identifier, string? Confirm) : IRequest;

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
        ILogger<DeleteTenantCommandHandler>? logger = null) : IRequestHandler<DeleteTenantCommand>
    {
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

            tenant.Status = TenantStatusNames.Deleting;
            await platform.SaveChangesAsync(cancellationToken);

            // Its addresses stop answering now, not when the job gets to them
            if (hostMap != null)
            {
                var hosts = await platform.TenantDomains.AsNoTracking().Where(d => d.TenantId == tenant.Id).Select(d => d.Host).ToListAsync(cancellationToken);
                foreach (var host in hosts)
                {
                    await hostMap.RemoveAsync(host, cancellationToken);
                }
            }

            await queue.EnqueueAsync(new TenantJobMessage(identifier, TenantJobCatalog.DeleteSite), cancellationToken);
            logger?.LogWarning("{Tenant} is being deleted", identifier);
        }
    }

    /// <summary>
    /// The queued half of deleting a site, run inside it: what is cached at the edge, its files, then every
    /// row it has. It refuses any site that hasn't been marked as being deleted, so no stray message can
    /// remove a live one. Each step removes what is there and is safe to run again; a failure leaves the site
    /// marked as being deleted, and deleting it again from the admin app finishes the job.
    ///
    /// Order: the edge first (nothing is served), then files (nothing is downloadable), then rows, the tenant's
    /// own last. If rows went first, nothing would be left to say whose the files were.
    /// </summary>
    public record PurgeSiteCommand : IRequest<string>;

    public class PurgeSiteCommandHandler(ElysianContext context, PlatformContext platform, ITenantDataEraser eraser,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor, ILogger<PurgeSiteCommandHandler> logger,
        IEdgeConfigStore? edge = null, IEdgeHostMap? hostMap = null, IPhotoStorage? photos = null, IPlacementStore? placements = null,
        ICdnPurger? purger = null) : IRequestHandler<PurgeSiteCommand, string>
    {
        public const string NotMarked = "Skipped: the site is not marked as being deleted";

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
