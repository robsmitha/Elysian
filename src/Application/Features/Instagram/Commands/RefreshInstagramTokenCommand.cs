using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Instagram.Commands
{
    /// <summary>
    /// Refreshes the stored Instagram token once it's at least Instagram:RefreshIntervalDays old. Meant to be
    /// run often (e.g. a daily timer): runs that aren't due are skipped, so restarts never cause extra refreshes.
    /// Never throws for Instagram or storage failures; they're logged and reported as <see cref="InstagramTokenRefreshResult.Failed"/>.
    /// </summary>
    /// <param name="Force">Refresh even if the interval hasn't passed. Tokens under 24 hours old are still skipped, since Instagram rejects them.</param>
    public record RefreshInstagramTokenCommand(bool Force = false) : IRequest<InstagramTokenRefreshResult>;

    public enum InstagramTokenRefreshResult
    {
        NotConfigured,
        Skipped,
        Refreshed,
        Failed,
    }

    public class RefreshInstagramTokenCommandHandler(IInstagramService instagramService, IInstagramTokenStore tokenStore,
        IOptions<InstagramSettings> options, TimeProvider timeProvider, ILogger<RefreshInstagramTokenCommandHandler> logger)
        : IRequestHandler<RefreshInstagramTokenCommand, InstagramTokenRefreshResult>
    {
        /// <summary>
        /// Instagram rejects refreshing a token less than 24 hours old
        /// </summary>
        private static readonly TimeSpan MinimumTokenAge = TimeSpan.FromHours(24);

        public async Task<InstagramTokenRefreshResult> Handle(RefreshInstagramTokenCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var token = await tokenStore.GetAsync(cancellationToken);
                if (token == null)
                {
                    logger.LogWarning("Instagram token refresh skipped: no token is configured ({Setting})",
                        $"{InstagramSettings.SectionName}:{nameof(InstagramSettings.AccessToken)}");
                    return InstagramTokenRefreshResult.NotConfigured;
                }

                var interval = TimeSpan.FromDays(options.Value.RefreshIntervalDays);
                if (interval < MinimumTokenAge)
                {
                    interval = MinimumTokenAge;
                }

                var age = timeProvider.GetUtcNow() - token.LastRefreshedUtc;
                var dueAfter = request.Force ? MinimumTokenAge : interval;
                if (age < dueAfter)
                {
                    logger.LogInformation("Instagram token refresh not due; last refreshed {LastRefreshedUtc:O}, next refresh after {DueUtc:O}",
                        token.LastRefreshedUtc, token.LastRefreshedUtc + dueAfter);
                    return InstagramTokenRefreshResult.Skipped;
                }

                var refreshed = await instagramService.RefreshTokenAsync(cancellationToken);
                logger.LogInformation("Instagram token refreshed at {RefreshedUtc:O}; next refresh after {DueUtc:O}",
                    refreshed.LastRefreshedUtc, refreshed.LastRefreshedUtc + interval);
                return InstagramTokenRefreshResult.Refreshed;
            }
            catch (InstagramApiException ex) when (ex.IsInvalidToken)
            {
                logger.LogError(InstagramApiException.InvalidTokenHelp);
                return InstagramTokenRefreshResult.Failed;
            }
            catch (InstagramApiException ex)
            {
                logger.LogError("Instagram token refresh failed; the current token stays in use. {Error}", ex.Message);
                return InstagramTokenRefreshResult.Failed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Unexpected error refreshing the Instagram token; the current token stays in use");
                return InstagramTokenRefreshResult.Failed;
            }
        }
    }
}
