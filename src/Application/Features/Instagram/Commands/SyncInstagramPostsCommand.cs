using Azure;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Instagram.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Instagram.Commands
{
    /// <summary>
    /// Mirrors the tenant's latest Instagram posts into <see cref="InstagramPost"/>: new posts get WebP thumbnails
    /// (Instagram:ThumbnailWidths) made by the photo processor and uploaded to the public photos container, existing
    /// posts get their caption and link updated, and posts that dropped out of the latest Instagram:PostCount are
    /// deleted along with their thumbnails. Meant to run on a timer. Never throws for Instagram, image or storage
    /// failures: a failed fetch leaves the mirror untouched, and a failed post is skipped and retried next run.
    /// </summary>
    public record SyncInstagramPostsCommand : IRequest<InstagramSyncResult>;

    public enum InstagramSyncStatus
    {
        NotConfigured,
        Synced,
        Failed,
    }

    public record InstagramSyncResult(InstagramSyncStatus Status, int Added = 0, int Updated = 0, int Removed = 0, int Failed = 0);

    public class SyncInstagramPostsCommandHandler(ElysianContext context, IInstagramService instagramService,
        IInstagramTokenStore tokenStore, IPhotoStorage photoStorage, IPhotoProcessor photoProcessor, IMemoryCache cache,
        IOptions<InstagramSettings> options, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        TimeProvider timeProvider, ILogger<SyncInstagramPostsCommandHandler> logger)
        : IRequestHandler<SyncInstagramPostsCommand, InstagramSyncResult>
    {
        // The timer and a manual connect can overlap; two syncs would race to insert the same posts
        private static readonly SemaphoreSlim SyncLock = new(1, 1);

        public async Task<InstagramSyncResult> Handle(SyncInstagramPostsCommand request, CancellationToken cancellationToken)
        {
            await SyncLock.WaitAsync(cancellationToken);
            try
            {
                return await SyncAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Unexpected error syncing Instagram posts; the current posts stay in place");
                return new InstagramSyncResult(InstagramSyncStatus.Failed);
            }
            finally
            {
                SyncLock.Release();
            }
        }

        private async Task<InstagramSyncResult> SyncAsync(CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            var token = await tokenStore.GetAsync(cancellationToken);
            if (token == null)
            {
                logger.LogInformation("Instagram sync skipped for tenant {Tenant}: no Instagram account is connected", tenantIdentifier);
                return new InstagramSyncResult(InstagramSyncStatus.NotConfigured);
            }

            var settings = options.Value;

            List<InstagramMedia> media;
            try
            {
                media = await instagramService.GetRecentMediaAsync(token.AccessToken, settings.PostCount, cancellationToken);
            }
            catch (InstagramApiException ex)
            {
                if (ex.IsInvalidToken)
                {
                    logger.LogError(InstagramApiException.InvalidTokenHelp + " The current posts stay in place until then.");
                }
                else
                {
                    logger.LogWarning("Instagram sync could not fetch posts; the current posts stay in place. {Error}", ex.Message);
                }
                return new InstagramSyncResult(InstagramSyncStatus.Failed);
            }

            var now = timeProvider.GetUtcNow();
            var existing = await context.InstagramPosts.ToDictionaryAsync(p => p.MediaId, cancellationToken);
            int added = 0, updated = 0, removed = 0, failed = 0;

            foreach (var item in media)
            {
                if (!InstagramPaths.IsValidMediaId(item.Id))
                {
                    logger.LogWarning("Skipping Instagram media with an unexpected id format");
                    failed++;
                    continue;
                }

                if (existing.TryGetValue(item.Id, out var post))
                {
                    post.Permalink = item.Permalink;
                    post.Caption = item.Caption;
                    post.MediaType = item.MediaType;
                    post.PostedAt = item.Timestamp;
                    post.SyncedAt = now;
                    updated++;
                    continue;
                }

                try
                {
                    context.InstagramPosts.Add(await CreatePostAsync(tenantIdentifier, item, settings.ThumbnailWidths, now, cancellationToken));
                    await context.SaveChangesAsync(cancellationToken);
                    added++;
                }
                catch (Exception ex) when (ex is InstagramApiException or PhotoProcessingException or RequestFailedException)
                {
                    // Retried next run, since the post still won't exist
                    logger.LogWarning("Instagram media {MediaId} was skipped: {Error}", item.Id, ex.Message);
                    failed++;
                }
            }

            // Only the latest PostCount are kept; anything else was deleted on Instagram or has scrolled out of the feed
            var latestIds = media.Select(m => m.Id).ToHashSet();
            foreach (var stale in existing.Values.Where(p => !latestIds.Contains(p.MediaId)))
            {
                await photoStorage.DeleteVariantsAsync(InstagramPaths.Prefix(tenantIdentifier, stale.MediaId), cancellationToken);
                context.InstagramPosts.Remove(stale);
                removed++;
            }

            await context.SaveChangesAsync(cancellationToken);
            cache.Remove(GetInstagramPostsQueryHandler.CacheKey(tenantIdentifier));

            logger.LogInformation("Instagram sync for tenant {Tenant}: {Added} added, {Updated} updated, {Removed} removed, {Failed} failed",
                tenantIdentifier, added, updated, removed, failed);
            return new InstagramSyncResult(InstagramSyncStatus.Synced, added, updated, removed, failed);
        }

        private async Task<InstagramPost> CreatePostAsync(string tenantIdentifier, InstagramMedia item, int[] widths,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            ProcessedPhoto processed;
            await using (var image = await instagramService.DownloadImageAsync(item.ImageUrl, cancellationToken))
            {
                processed = await photoProcessor.ProcessAsync(image, widths, cancellationToken);
            }

            foreach (var variant in processed.Variants)
            {
                await photoStorage.UploadVariantAsync(InstagramPaths.Variant(tenantIdentifier, item.Id, variant.Width),
                    variant.Content, variant.ContentType, cancellationToken);
            }

            return new InstagramPost
            {
                MediaId = item.Id,
                Permalink = item.Permalink,
                Caption = item.Caption,
                MediaType = item.MediaType,
                PostedAt = item.Timestamp,
                Width = processed.Width,
                Height = processed.Height,
                Placeholder = processed.Placeholder,
                VariantWidths = processed.Variants.Select(v => v.Width).OrderBy(w => w).ToList(),
                SyncedAt = now,
            };
        }
    }
}
