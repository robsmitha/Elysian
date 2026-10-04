using Elysian.Application.Exceptions;
using Elysian.Application.Features.GoogleCalendar.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.GoogleCalendar.Queries
{
    /// <summary>
    /// Connection status for the admin Calendar page, checked live against Google (and recorded as validated when it works)
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoRead)]
    public record GetGoogleCalendarConnectionQuery : IRequest<GoogleCalendarConnectionModel>;

    public class GetGoogleCalendarConnectionQueryHandler(IGoogleTokenStore tokenStore, IGoogleCalendarService calendarService,
        TimeProvider timeProvider, ILogger<GetGoogleCalendarConnectionQueryHandler> logger)
        : IRequestHandler<GetGoogleCalendarConnectionQuery, GoogleCalendarConnectionModel>
    {
        public async Task<GoogleCalendarConnectionModel> Handle(GetGoogleCalendarConnectionQuery request, CancellationToken cancellationToken)
        {
            var token = await tokenStore.GetAsync(cancellationToken);
            if (token == null)
            {
                return new GoogleCalendarConnectionModel(false, null, null, null, null);
            }

            string? error = null;
            try
            {
                await calendarService.VerifyAccessAsync(cancellationToken);
                token = await tokenStore.MarkValidatedAsync(timeProvider.GetUtcNow(), cancellationToken) ?? token;
            }
            catch (CalendarAuthorizationException ex)
            {
                error = ex.Message;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Google Calendar connection check failed");
                error = "Google Calendar couldn't be reached to check the connection. Try again in a few minutes.";
            }

            return new GoogleCalendarConnectionModel(true, token.AccountId, token.ConnectedUtc, token.LastValidatedUtc, error);
        }
    }
}
