using Elysian.Application.Features.Instagram.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Instagram.Queries
{
    /// <summary>
    /// Public, anonymous read of the tenant's mirrored Instagram posts, newest first. Reads the database only;
    /// Instagram is never called on a page load, so its outages just mean the mirror stops updating.
    /// </summary>
    public record GetInstagramPostsQuery : IRequest<List<InstagramPostModel>>;

    public class GetInstagramPostsQueryHandler(ElysianContext context, IPhotoStorage photoStorage, IMemoryCache cache,
        IOptions<InstagramSettings> options, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetInstagramPostsQuery, List<InstagramPostModel>>
    {
        /// <summary>
        /// Cleared by SyncInstagramPostsCommand so new posts show up right away on this instance
        /// </summary>
        public static string CacheKey(string tenantIdentifier) => $"instagram:posts:{tenantIdentifier}";

        public async Task<List<InstagramPostModel>> Handle(GetInstagramPostsQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            var settings = options.Value;

            return await cache.GetOrCreateAsync(CacheKey(tenantIdentifier), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(settings.CacheMinutes, 1));

                var posts = await context.InstagramPosts.AsNoTracking()
                    .OrderByDescending(p => p.PostedAt)
                    .ThenByDescending(p => p.InstagramPostId)
                    .Take(settings.PostCount)
                    .ToListAsync(cancellationToken);

                return posts.Select(p => new InstagramPostModel(p.MediaId, p.Permalink, p.Caption, p.MediaType, p.PostedAt,
                    p.Width, p.Height, p.Placeholder, photoStorage.GetPublicUrl(InstagramPaths.Folder(tenantIdentifier, p.MediaId)),
                    p.VariantWidths)).ToList();
            }) ?? [];
        }
    }
}
