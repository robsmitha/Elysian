using System.Security.Cryptography;
using System.Text;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.GoogleCalendar.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.GoogleCalendar.Commands
{
    /// <summary>
    /// The "state" value that travels to Google and back. It names the site, so the one return page shared by every
    /// tenant knows whose connection this is, and ends in a random value that is stored against the person who started
    /// and accepted once, within <see cref="Lifetime"/>.
    /// </summary>
    public static class GoogleCalendarConnectionState
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        public static string Create(string siteIdentifier) =>
            $"{siteIdentifier}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";

        public static bool Matches(string stored, string returned) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(returned));
    }

    /// <summary>
    /// Step one of connecting a calendar: where to send the photographer so they can allow access at Google
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record StartGoogleCalendarConnectionCommand : IRequest<GoogleCalendarAuthorizationModel>;

    public class StartGoogleCalendarConnectionCommandHandler(ElysianContext context, IGoogleOAuthClient oauthClient,
        IGoogleTokenStore tokenStore, IClaimsPrincipalAccessor claimsPrincipalAccessor,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor, TimeProvider timeProvider)
        : IRequestHandler<StartGoogleCalendarConnectionCommand, GoogleCalendarAuthorizationModel>
    {
        public async Task<GoogleCalendarAuthorizationModel> Handle(StartGoogleCalendarConnectionCommand request, CancellationToken cancellationToken)
        {
            if (!oauthClient.CanAuthorize)
            {
                throw new CustomValidationException([new ValidationFailure("GoogleCalendar",
                    "Connecting a Google Calendar isn't set up on this platform yet.")]);
            }

            var userId = claimsPrincipalAccessor.UserId!;
            var now = timeProvider.GetUtcNow().UtcDateTime;

            // One attempt at a time per person, and nothing left behind by attempts that were abandoned
            var stale = await context.OAuthStates
                .Where(s => s.OAuthProvider == OAuthProviders.Google
                    && (s.UserId == userId || s.CreatedAt < now - GoogleCalendarConnectionState.Lifetime))
                .ToListAsync(cancellationToken);
            context.OAuthStates.RemoveRange(stale);

            var state = GoogleCalendarConnectionState.Create(multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!);
            context.OAuthStates.Add(new OAuthState { OAuthProvider = OAuthProviders.Google, UserId = userId, State = state, CreatedAt = now });
            await context.SaveChangesAsync(cancellationToken);

            // Reconnecting: offer the account that was connected before
            var previous = (await tokenStore.GetAsync(cancellationToken))?.AccountId;
            var hint = previous?.Contains('@') == true && !previous.EndsWith("calendar.google.com", StringComparison.OrdinalIgnoreCase)
                ? previous
                : null;

            return new GoogleCalendarAuthorizationModel(oauthClient.AuthorizationUrl(state, hint));
        }
    }

    /// <summary>
    /// Step two: Google sent the photographer back with a code. It is exchanged for tokens and the calendar is read
    /// before anything is stored, so a failed attempt never replaces a working connection.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record CompleteGoogleCalendarConnectionCommand(string Code, string State) : IRequest<GoogleCalendarConnectionModel>
    {
        // Request logging prints commands; the code is a credential until it is used
        public override string ToString() => $"{nameof(CompleteGoogleCalendarConnectionCommand)} {{ Code = [redacted] }}";
    }

    public class CompleteGoogleCalendarConnectionCommandHandler(ElysianContext context, IGoogleOAuthClient oauthClient,
        IGoogleCalendarService calendarService, IGoogleTokenStore tokenStore, IClaimsPrincipalAccessor claimsPrincipalAccessor,
        TimeProvider timeProvider, ILogger<CompleteGoogleCalendarConnectionCommandHandler> logger)
        : IRequestHandler<CompleteGoogleCalendarConnectionCommand, GoogleCalendarConnectionModel>
    {
        public const string TryAgain = "That sign-in can't be used any more. Choose Connect Google Calendar to start again.";

        public async Task<GoogleCalendarConnectionModel> Handle(CompleteGoogleCalendarConnectionCommand request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State))
            {
                throw Invalid(TryAgain);
            }

            // The attempt this person started, on this site. Used up now, whatever happens next.
            var userId = claimsPrincipalAccessor.UserId!;
            var now = timeProvider.GetUtcNow();
            var attempts = await context.OAuthStates
                .Where(s => s.OAuthProvider == OAuthProviders.Google && s.UserId == userId)
                .ToListAsync(cancellationToken);
            var attempt = attempts.FirstOrDefault(s => GoogleCalendarConnectionState.Matches(s.State, request.State));
            if (attempt == null)
            {
                throw Invalid(TryAgain);
            }

            context.OAuthStates.Remove(attempt);
            await context.SaveChangesAsync(cancellationToken);

            if (attempt.CreatedAt < now.UtcDateTime - GoogleCalendarConnectionState.Lifetime)
            {
                throw Invalid(TryAgain);
            }

            GoogleAuthorization granted;
            string calendarName;
            try
            {
                granted = await oauthClient.ExchangeCodeAsync(request.Code, cancellationToken);

                // Google lets a person untick any permission on its screen
                if (!GoogleCalendarScopes.AllowsBooking(granted.Scope))
                {
                    await RevokeQuietlyAsync(granted, cancellationToken);
                    throw Invalid("Access to your calendar wasn't allowed. Connect again and leave the calendar permission ticked on Google's screen.");
                }

                if (granted.RefreshToken == null)
                {
                    throw Invalid("Google didn't give lasting access. Choose Connect Google Calendar to try again.");
                }

                calendarName = await calendarService.VerifyAccessAsync(granted.AccessToken, cancellationToken);
            }
            catch (CalendarAuthorizationException ex) when (ex.Failure == CalendarAuthorizationFailure.Revoked)
            {
                throw Invalid(TryAgain);
            }
            catch (CalendarAuthorizationException ex)
            {
                throw Invalid(ex.Message);
            }
            catch (Exception ex) when (ex is not CustomValidationException && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
            {
                logger.LogWarning(ex, "Google Calendar connection could not be completed");
                throw Invalid("Google couldn't be reached to finish connecting. Please try again in a few minutes.");
            }

            var accountId = string.IsNullOrWhiteSpace(granted.Email) ? calendarName : granted.Email;
            await tokenStore.SaveAsync(new GoogleToken(granted.RefreshToken, accountId, granted.Scope, granted.AccessToken,
                now + granted.ExpiresIn, now, now), cancellationToken);
            logger.LogInformation("Google Calendar connected for {AccountId}", accountId);

            return new GoogleCalendarConnectionModel(true, accountId, now, now, null);
        }

        /// <summary>
        /// A grant that can't be used shouldn't be left standing at Google
        /// </summary>
        private async Task RevokeQuietlyAsync(GoogleAuthorization granted, CancellationToken cancellationToken)
        {
            try
            {
                await oauthClient.RevokeAsync(granted.RefreshToken ?? granted.AccessToken, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "An unusable Google grant could not be withdrawn");
            }
        }

        private static CustomValidationException Invalid(string message) => new([new ValidationFailure("GoogleCalendar", message)]);
    }

    /// <summary>
    /// Disconnects the tenant's calendar: the grant is withdrawn at Google and the tokens are forgotten, so the platform
    /// holds no access it doesn't use. Online booking stops until a calendar is connected again.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record DisconnectGoogleCalendarCommand : IRequest<GoogleCalendarConnectionModel>;

    public class DisconnectGoogleCalendarCommandHandler(IGoogleOAuthClient oauthClient, IGoogleTokenStore tokenStore,
        ILogger<DisconnectGoogleCalendarCommandHandler> logger)
        : IRequestHandler<DisconnectGoogleCalendarCommand, GoogleCalendarConnectionModel>
    {
        public async Task<GoogleCalendarConnectionModel> Handle(DisconnectGoogleCalendarCommand request, CancellationToken cancellationToken)
        {
            var token = await tokenStore.GetAsync(cancellationToken);
            if (token != null)
            {
                try
                {
                    await oauthClient.RevokeAsync(token.RefreshToken, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // Already withdrawn by the photographer, issued by another client, or Google is unreachable. The
                    // tokens are forgotten either way; the photographer can remove the access in their Google account.
                    logger.LogWarning(ex, "Google did not confirm withdrawing access for {AccountId}", token.AccountId);
                }

                logger.LogInformation("Google Calendar disconnected for {AccountId}", token.AccountId);
            }

            await tokenStore.DeleteAsync(cancellationToken);
            return new GoogleCalendarConnectionModel(false, null, null, null, null);
        }
    }
}
