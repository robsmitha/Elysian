using Azure;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Instagram.Models;
using Elysian.Application.Features.Instagram.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Instagram.Commands
{
    /// <summary>
    /// Step one of connecting Instagram: where to send the photographer so they can approve on Instagram
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record StartInstagramConnectionCommand : IRequest<InstagramAuthorizationModel>;

    public class StartInstagramConnectionCommandHandler(ElysianContext context, IInstagramOAuthClient oauthClient,
        IClaimsPrincipalAccessor claimsPrincipalAccessor, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        TimeProvider timeProvider)
        : IRequestHandler<StartInstagramConnectionCommand, InstagramAuthorizationModel>
    {
        public async Task<InstagramAuthorizationModel> Handle(StartInstagramConnectionCommand request, CancellationToken cancellationToken)
        {
            if (!oauthClient.CanAuthorize)
            {
                throw new CustomValidationException([new ValidationFailure("Instagram",
                    "Connecting Instagram isn't set up on this platform yet.")]);
            }

            var userId = claimsPrincipalAccessor.UserId!;
            var now = timeProvider.GetUtcNow().UtcDateTime;

            // One attempt at a time per person, and nothing left behind by attempts that were abandoned
            var stale = await context.OAuthStates
                .Where(s => s.OAuthProvider == OAuthProviders.Instagram
                    && (s.UserId == userId || s.CreatedAt < now - OAuthConnectionState.Lifetime))
                .ToListAsync(cancellationToken);
            context.OAuthStates.RemoveRange(stale);

            var state = OAuthConnectionState.Create(multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!);
            context.OAuthStates.Add(new OAuthState { OAuthProvider = OAuthProviders.Instagram, UserId = userId, State = state, CreatedAt = now });
            await context.SaveChangesAsync(cancellationToken);

            return new InstagramAuthorizationModel(oauthClient.AuthorizationUrl(state));
        }
    }

    /// <summary>
    /// Step two: Instagram sent the photographer back with a code. It is exchanged for a long-lived token and the
    /// account is read before anything is stored, so a failed attempt never replaces a working connection. The posts
    /// are synced right away.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record CompleteInstagramConnectionCommand(string Code, string State) : IRequest<InstagramConnectionModel>
    {
        // Request logging prints commands; the code is a credential until it is used
        public override string ToString() => $"{nameof(CompleteInstagramConnectionCommand)} {{ Code = [redacted] }}";
    }

    public class CompleteInstagramConnectionCommandHandler(ElysianContext context, IInstagramOAuthClient oauthClient,
        IInstagramService instagramService, IInstagramTokenStore tokenStore, IClaimsPrincipalAccessor claimsPrincipalAccessor,
        ISender mediator, TimeProvider timeProvider, ILogger<CompleteInstagramConnectionCommandHandler> logger)
        : IRequestHandler<CompleteInstagramConnectionCommand, InstagramConnectionModel>
    {
        public const string TryAgain = "That sign-in can't be used any more. Choose Connect Instagram to start again.";

        public async Task<InstagramConnectionModel> Handle(CompleteInstagramConnectionCommand request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State))
            {
                throw Invalid(TryAgain);
            }

            // The attempt this person started, on this site. Used up now, whatever happens next.
            var userId = claimsPrincipalAccessor.UserId!;
            var now = timeProvider.GetUtcNow();
            var attempts = await context.OAuthStates
                .Where(s => s.OAuthProvider == OAuthProviders.Instagram && s.UserId == userId)
                .ToListAsync(cancellationToken);
            var attempt = attempts.FirstOrDefault(s => OAuthConnectionState.Matches(s.State, request.State));
            if (attempt == null)
            {
                throw Invalid(TryAgain);
            }

            context.OAuthStates.Remove(attempt);
            await context.SaveChangesAsync(cancellationToken);

            if (attempt.CreatedAt < now.UtcDateTime - OAuthConnectionState.Lifetime)
            {
                throw Invalid(TryAgain);
            }

            InstagramAuthorization granted;
            InstagramAccount account;
            try
            {
                granted = await oauthClient.ExchangeCodeAsync(request.Code, cancellationToken);

                // Without this permission the posts can't be read and the token can't be refreshed
                if (granted.Permissions.Count > 0 && !granted.Permissions.Contains(InstagramPermissions.Basic))
                {
                    throw Invalid("Instagram didn't allow access to your profile and posts. Choose Connect Instagram and allow it on Instagram's screen.");
                }

                account = await instagramService.GetAccountAsync(granted.AccessToken, cancellationToken);
            }
            catch (InstagramApiException ex) when (ex.StatusCode == 400 || ex.IsInvalidToken)
            {
                // The code was used already, has expired, or was issued for another app
                logger.LogWarning("Instagram refused the sign-in: {Error}", ex.Message);
                throw Invalid(TryAgain);
            }
            catch (Exception ex) when (ex is not CustomValidationException && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
            {
                logger.LogWarning(ex, "Instagram connection could not be completed");
                throw Invalid("Instagram couldn't be reached to finish connecting. Please try again in a few minutes.");
            }

            var expires = granted.ExpiresIn is TimeSpan lifetime ? now + lifetime : (DateTimeOffset?)null;
            await tokenStore.SaveAsync(new InstagramToken(granted.AccessToken, account.UserId, now, expires), cancellationToken);
            logger.LogInformation("Instagram account {Username} ({UserId}) connected", account.Username, account.UserId);

            await mediator.Send(new SyncInstagramPostsCommand(), cancellationToken);

            var postCount = await context.InstagramPosts.CountAsync(cancellationToken);
            var lastSynced = await context.InstagramPosts.MaxAsync(p => (DateTimeOffset?)p.SyncedAt, cancellationToken);
            return new InstagramConnectionModel(true, account.Username, account.UserId, now, expires, lastSynced, postCount, null);
        }

        private static CustomValidationException Invalid(string message) => new([new ValidationFailure("Instagram", message)]);
    }

    /// <summary>
    /// Disconnects the tenant's Instagram account: the token is forgotten and so are the mirrored posts and their
    /// thumbnails, so the site stops showing the feed. Instagram keeps listing the app under the account's connected
    /// apps until the photographer removes it there; the platform holds nothing it could use in the meantime.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record DisconnectInstagramCommand : IRequest<InstagramConnectionModel>;

    public class DisconnectInstagramCommandHandler(ElysianContext context, IInstagramTokenStore tokenStore, IPhotoStorage photoStorage,
        IMemoryCache cache, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        ILogger<DisconnectInstagramCommandHandler> logger)
        : IRequestHandler<DisconnectInstagramCommand, InstagramConnectionModel>
    {
        public async Task<InstagramConnectionModel> Handle(DisconnectInstagramCommand request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;

            // A sync that is part way through would put posts back
            await SyncInstagramPostsCommandHandler.SyncLock.WaitAsync(cancellationToken);
            try
            {
                await tokenStore.DeleteAsync(cancellationToken);

                var posts = await context.InstagramPosts.ToListAsync(cancellationToken);
                context.InstagramPosts.RemoveRange(posts);
                await context.SaveChangesAsync(cancellationToken);
                cache.Remove(GetInstagramPostsQueryHandler.CacheKey(tenantIdentifier));

                try
                {
                    await photoStorage.DeleteVariantsAsync(InstagramPaths.TenantPrefix(tenantIdentifier), cancellationToken);
                }
                catch (RequestFailedException ex)
                {
                    // Nothing points at the files any more; they are only taking up space
                    logger.LogWarning("Instagram thumbnails for tenant {Tenant} could not be deleted: {Error}", tenantIdentifier, ex.Message);
                }

                logger.LogInformation("Instagram disconnected for tenant {Tenant}; {Posts} mirrored posts removed", tenantIdentifier, posts.Count);
            }
            finally
            {
                SyncInstagramPostsCommandHandler.SyncLock.Release();
            }

            return new InstagramConnectionModel(false, null, null, null, null, null, 0, null);
        }
    }
}
