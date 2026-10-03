using Elysian.Application.Exceptions;
using Elysian.Application.Features.Instagram.Models;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Instagram.Queries
{
    /// <summary>
    /// Public, anonymous read of the most recent Instagram posts. Served from memory for Instagram:CacheMinutes;
    /// when Instagram fails (outage, rate limit, expired token) the last successful result keeps being served,
    /// so the response is never an error and only empty if Instagram has never answered since startup.
    /// </summary>
    public record GetInstagramPostsQuery : IRequest<List<InstagramPostModel>>;

    public class GetInstagramPostsQueryHandler(IInstagramService instagramService, IMemoryCache cache,
        IOptions<InstagramSettings> options, ILogger<GetInstagramPostsQueryHandler> logger)
        : IRequestHandler<GetInstagramPostsQuery, List<InstagramPostModel>>
    {
        private const string CacheKey = "instagram:posts";
        private const string LastGoodCacheKey = "instagram:posts:last-good";

        /// <summary>
        /// How long a failure is remembered before Instagram is asked again, so an outage or rate limit
        /// isn't retried on every page view
        /// </summary>
        private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(5);

        // Collapses concurrent cache misses into a single call to Instagram
        private static readonly SemaphoreSlim Refresh = new(1, 1);

        public async Task<List<InstagramPostModel>> Handle(GetInstagramPostsQuery request, CancellationToken cancellationToken)
        {
            if (cache.TryGetValue(CacheKey, out List<InstagramPostModel>? cached) && cached != null)
            {
                return cached;
            }

            await Refresh.WaitAsync(cancellationToken);
            try
            {
                if (cache.TryGetValue(CacheKey, out cached) && cached != null)
                {
                    return cached;
                }

                var settings = options.Value;
                try
                {
                    var posts = await instagramService.GetRecentPostsAsync(settings.PostCount, cancellationToken);

                    cache.Set(CacheKey, posts, TimeSpan.FromMinutes(Math.Max(settings.CacheMinutes, 1)));
                    cache.Set(LastGoodCacheKey, posts, new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
                    return posts;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    LogFailure(ex);

                    var fallback = cache.Get<List<InstagramPostModel>>(LastGoodCacheKey) ?? [];
                    var backoff = TimeSpan.FromMinutes(Math.Min(FailureBackoff.TotalMinutes, Math.Max(settings.CacheMinutes, 1)));
                    cache.Set(CacheKey, fallback, backoff);
                    return fallback;
                }
            }
            finally
            {
                Refresh.Release();
            }
        }

        private void LogFailure(Exception ex)
        {
            switch (ex)
            {
                case InstagramApiException { IsInvalidToken: true }:
                    logger.LogError(InstagramApiException.InvalidTokenHelp + " Serving the last successful posts until then.");
                    break;
                case InstagramApiException { IsRateLimited: true } apiException:
                    logger.LogWarning("Instagram rate limited the posts request; serving the last successful posts. {Error}", apiException.Message);
                    break;
                case InstagramApiException apiException:
                    logger.LogWarning("Instagram posts request failed; serving the last successful posts. {Error}", apiException.Message);
                    break;
                default:
                    logger.LogError(ex, "Unexpected error loading Instagram posts; serving the last successful posts");
                    break;
            }
        }
    }
}
