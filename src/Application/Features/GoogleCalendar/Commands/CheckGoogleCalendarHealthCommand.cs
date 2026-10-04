using Elysian.Application.Exceptions;
using Elysian.Application.Features.Booking;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.GoogleCalendar.Commands
{
    /// <summary>
    /// Scheduled health check: forces a token refresh (saving a rotated refresh token if Google issues one), reads the
    /// calendar, and records the time it last worked. Google refresh tokens don't need refreshing on a schedule, so this
    /// only proves access still works. Auth failures are logged as errors and emailed to the photographer.
    /// Never throws for Google or storage failures; they're logged and reported as <see cref="GoogleCalendarHealthResult.Failed"/>.
    /// </summary>
    public record CheckGoogleCalendarHealthCommand : IRequest<GoogleCalendarHealthResult>;

    public enum GoogleCalendarHealthResult
    {
        NotConnected,
        Healthy,
        Failed,
    }

    public class CheckGoogleCalendarHealthCommandHandler(IGoogleTokenStore tokenStore, IGoogleAccessTokenProvider tokenProvider,
        IGoogleCalendarService calendarService, IEmailService emailService, IOptions<BookingSettings> bookingOptions,
        IOptions<ResendSettings> resendOptions, TimeProvider timeProvider, ILogger<CheckGoogleCalendarHealthCommandHandler> logger)
        : IRequestHandler<CheckGoogleCalendarHealthCommand, GoogleCalendarHealthResult>
    {
        public async Task<GoogleCalendarHealthResult> Handle(CheckGoogleCalendarHealthCommand request, CancellationToken cancellationToken)
        {
            try
            {
                if (await tokenStore.GetAsync(cancellationToken) == null)
                {
                    logger.LogWarning("Google Calendar health check skipped: no calendar is connected, so online booking is unavailable");
                    return GoogleCalendarHealthResult.NotConnected;
                }

                await tokenProvider.GetAccessTokenAsync(forceRefresh: true, cancellationToken);
                var accountId = await calendarService.VerifyAccessAsync(cancellationToken);
                await tokenStore.MarkValidatedAsync(timeProvider.GetUtcNow(), cancellationToken);

                logger.LogInformation("Google Calendar health check passed for {AccountId}", accountId);
                return GoogleCalendarHealthResult.Healthy;
            }
            catch (CalendarAuthorizationException ex)
            {
                logger.LogError("Google Calendar health check failed ({Failure}); online booking is unavailable. {Message}", ex.Failure, ex.Message);
                await AlertAsync(ex.Message, cancellationToken);
                return GoogleCalendarHealthResult.Failed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Usually a passing outage; the next run retries, so this isn't emailed
                logger.LogError(ex, "Unexpected error checking Google Calendar access");
                return GoogleCalendarHealthResult.Failed;
            }
        }

        private async Task AlertAsync(string problem, CancellationToken cancellationToken)
        {
            var to = BookingEmails.OwnerAddress(bookingOptions.Value, resendOptions.Value);
            if (to == null)
            {
                logger.LogWarning("No address to alert about the Google Calendar problem (Booking:NotificationEmailAddress or Resend:ToEmailAddress)");
                return;
            }

            try
            {
                await emailService.SendAsync(BookingEmails.CalendarNeedsAttention(to, problem, bookingOptions.Value), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Google Calendar alert email failed");
            }
        }
    }
}
