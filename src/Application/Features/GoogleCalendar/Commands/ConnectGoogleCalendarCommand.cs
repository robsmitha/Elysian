using Elysian.Application.Exceptions;
using Elysian.Application.Features.GoogleCalendar.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.GoogleCalendar.Commands
{
    /// <summary>
    /// Connects (or replaces) the tenant's Google Calendar with a refresh token. The token is exchanged and the calendar
    /// read before anything is stored, so a bad token never replaces a working one.
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record ConnectGoogleCalendarCommand(string RefreshToken) : IRequest<GoogleCalendarConnectionModel>
    {
        // Request logging prints commands; the token must never appear there
        public override string ToString() => $"{nameof(ConnectGoogleCalendarCommand)} {{ RefreshToken = [redacted] }}";
    }

    public class ConnectGoogleCalendarCommandValidator : AbstractValidator<ConnectGoogleCalendarCommand>
    {
        public ConnectGoogleCalendarCommandValidator()
        {
            RuleFor(v => v.RefreshToken)
                .NotEmpty().WithMessage("Paste the refresh token for the Google account that owns the calendar.")
                .MaximumLength(2048).WithMessage("That doesn't look like a Google refresh token.");
        }
    }

    public class ConnectGoogleCalendarCommandHandler(IGoogleOAuthClient oauthClient, IGoogleCalendarService calendarService,
        IGoogleTokenStore tokenStore, TimeProvider timeProvider, ILogger<ConnectGoogleCalendarCommandHandler> logger)
        : IRequestHandler<ConnectGoogleCalendarCommand, GoogleCalendarConnectionModel>
    {
        public async Task<GoogleCalendarConnectionModel> Handle(ConnectGoogleCalendarCommand request, CancellationToken cancellationToken)
        {
            var refreshToken = request.RefreshToken.Trim();

            RefreshedGoogleToken refreshed;
            string accountId;
            try
            {
                refreshed = await oauthClient.RefreshAsync(refreshToken, cancellationToken);
                accountId = await calendarService.VerifyAccessAsync(refreshed.AccessToken, cancellationToken);
            }
            catch (CalendarAuthorizationException ex) when (ex.Failure == CalendarAuthorizationFailure.Revoked)
            {
                throw Invalid("Google rejected this refresh token. Generate a new one for the calendar account and try again.");
            }
            catch (CalendarAuthorizationException ex)
            {
                throw Invalid(ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Google Calendar token could not be verified");
                throw Invalid("Google couldn't verify the token right now. Please try again in a few minutes.");
            }

            var now = timeProvider.GetUtcNow();
            await tokenStore.SaveAsync(new GoogleToken(refreshed.RefreshToken ?? refreshToken, accountId, refreshed.Scope,
                refreshed.AccessToken, now + refreshed.ExpiresIn, now, now), cancellationToken);
            logger.LogInformation("Google Calendar connected for {AccountId}", accountId);

            return new GoogleCalendarConnectionModel(true, accountId, now, now, null);
        }

        private static CustomValidationException Invalid(string message) =>
            new([new ValidationFailure(nameof(ConnectGoogleCalendarCommand.RefreshToken), message)]);
    }
}
