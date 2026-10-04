using Elysian.Application.Exceptions;
using Elysian.Application.Features.Instagram.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Instagram.Queries
{
    /// <summary>
    /// Admin status of the tenant's Instagram connection. Checks the stored token with Instagram (cached for an hour
    /// when it works) so an expired token shows up here, not just in the logs. Never returns the token.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoRead)]
    public record GetInstagramConnectionQuery : IRequest<InstagramConnectionModel>;

    public class GetInstagramConnectionQueryHandler(ElysianContext context, IInstagramTokenStore tokenStore,
        IInstagramService instagramService, IMemoryCache cache, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        ILogger<GetInstagramConnectionQueryHandler> logger)
        : IRequestHandler<GetInstagramConnectionQuery, InstagramConnectionModel>
    {
        private static readonly TimeSpan AccountCacheLifetime = TimeSpan.FromHours(1);

        public async Task<InstagramConnectionModel> Handle(GetInstagramConnectionQuery request, CancellationToken cancellationToken)
        {
            var postCount = await context.InstagramPosts.CountAsync(cancellationToken);
            var lastSynced = await context.InstagramPosts.MaxAsync(p => (DateTimeOffset?)p.SyncedAt, cancellationToken);

            var token = await tokenStore.GetAsync(cancellationToken);
            if (token == null)
            {
                return new InstagramConnectionModel(false, null, null, null, null, lastSynced, postCount, null);
            }

            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            // Keyed on the token's refresh time too, so a reconnect or refresh is checked again right away
            var cacheKey = $"instagram:account:{tenantIdentifier}:{token.UserId}:{token.LastRefreshedUtc.UtcTicks}";
            string? error = null;
            if (!cache.TryGetValue(cacheKey, out InstagramAccount? account))
            {
                try
                {
                    account = await instagramService.GetAccountAsync(token.AccessToken, cancellationToken);
                    cache.Set(cacheKey, account, AccountCacheLifetime);
                }
                catch (InstagramApiException ex) when (ex.IsInvalidToken)
                {
                    error = "Instagram rejected the stored token (error 190). Generate a new long-lived token in the Meta app dashboard and connect again.";
                }
                catch (InstagramApiException ex)
                {
                    logger.LogWarning("Instagram connection check failed: {Error}", ex.Message);
                    error = "Instagram couldn't be reached to check the connection.";
                }
            }

            return new InstagramConnectionModel(true, account?.Username, token.UserId, token.LastRefreshedUtc, token.ExpiresUtc,
                lastSynced, postCount, error);
        }
    }
}
