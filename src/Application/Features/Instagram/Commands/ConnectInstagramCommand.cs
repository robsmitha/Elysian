using Elysian.Application.Exceptions;
using Elysian.Application.Features.Instagram.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Instagram.Commands
{
    /// <summary>
    /// Connects (or replaces) the tenant's Instagram account with a long-lived token generated in the Meta app
    /// dashboard. The token is verified with Instagram before it's stored, then the posts are synced right away.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record ConnectInstagramCommand(string AccessToken) : IRequest<InstagramConnectionModel>
    {
        // Request logging prints commands; the token must never appear there
        public override string ToString() => $"{nameof(ConnectInstagramCommand)} {{ AccessToken = [redacted] }}";
    }

    public class ConnectInstagramCommandValidator : AbstractValidator<ConnectInstagramCommand>
    {
        public ConnectInstagramCommandValidator()
        {
            RuleFor(v => v.AccessToken)
                .NotEmpty().WithMessage("Paste the long-lived access token from the Meta app dashboard.")
                .MaximumLength(1024).WithMessage("That doesn't look like an Instagram access token.");
        }
    }

    public class ConnectInstagramCommandHandler(ElysianContext context, IInstagramService instagramService, IInstagramTokenStore tokenStore,
        ISender mediator, TimeProvider timeProvider, ILogger<ConnectInstagramCommandHandler> logger)
        : IRequestHandler<ConnectInstagramCommand, InstagramConnectionModel>
    {
        public async Task<InstagramConnectionModel> Handle(ConnectInstagramCommand request, CancellationToken cancellationToken)
        {
            var accessToken = request.AccessToken.Trim();

            InstagramAccount account;
            try
            {
                account = await instagramService.GetAccountAsync(accessToken, cancellationToken);
            }
            catch (InstagramApiException ex) when (ex.IsInvalidToken)
            {
                throw Invalid("Instagram rejected this token. Generate a new long-lived token in the Meta app dashboard and try again.");
            }
            catch (InstagramApiException ex)
            {
                logger.LogWarning("Instagram token could not be verified: {Error}", ex.Message);
                throw Invalid("Instagram couldn't verify the token right now. Please try again in a few minutes.");
            }

            // A pasted token's age is unknown, so its expiry is only known after the first refresh
            var connectedAt = timeProvider.GetUtcNow();
            await tokenStore.SaveAsync(new InstagramToken(accessToken, account.UserId, connectedAt, null), cancellationToken);
            logger.LogInformation("Instagram account {Username} ({UserId}) connected", account.Username, account.UserId);

            await mediator.Send(new SyncInstagramPostsCommand(), cancellationToken);

            var postCount = await context.InstagramPosts.CountAsync(cancellationToken);
            var lastSynced = await context.InstagramPosts.MaxAsync(p => (DateTimeOffset?)p.SyncedAt, cancellationToken);
            return new InstagramConnectionModel(true, account.Username, account.UserId, connectedAt, null, lastSynced, postCount, null);
        }

        private static CustomValidationException Invalid(string message) =>
            new([new ValidationFailure(nameof(ConnectInstagramCommand.AccessToken), message)]);
    }
}
