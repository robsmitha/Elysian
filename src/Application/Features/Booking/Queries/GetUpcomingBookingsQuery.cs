using Elysian.Application.Exceptions;
using Elysian.Application.Features.Booking.Commands;
using Elysian.Application.Features.Booking.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Booking.Queries
{
    /// <summary>
    /// The sessions booked through the site that start in the coming days, for the admin overview. Only events online
    /// booking created are read (<see cref="CreateBookingCommandHandler.SourceProperty"/>), never the photographer's own.
    /// </summary>
    /// <param name="Days">How far ahead to look, 1 to <see cref="GetUpcomingBookingsQueryHandler.MaxDays"/></param>
    [Authorize(Policy = PolicyNames.PhotoRead)]
    public record GetUpcomingBookingsQuery(int Days = 30) : IRequest<UpcomingBookingsModel>;

    public class GetUpcomingBookingsQueryHandler(IGoogleCalendarService calendarService, TimeProvider timeProvider,
        ILogger<GetUpcomingBookingsQueryHandler> logger)
        : IRequestHandler<GetUpcomingBookingsQuery, UpcomingBookingsModel>
    {
        public const int MaxDays = 90;

        public async Task<UpcomingBookingsModel> Handle(GetUpcomingBookingsQuery request, CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();
            var until = now.AddDays(Math.Clamp(request.Days, 1, MaxDays));

            List<TaggedCalendarEvent> events;
            try
            {
                events = await calendarService.GetTaggedEventsAsync(now, until, CreateBookingCommandHandler.SourceProperty, "true",
                    cancellationToken);
            }
            catch (CalendarAuthorizationException)
            {
                // Not connected, or the connection needs attention. The Calendar page says which; the overview just has no list.
                return new UpcomingBookingsModel(false, []);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Upcoming bookings couldn't be read from Google Calendar");
                return new UpcomingBookingsModel(false, []);
            }

            var bookings = events
                // The range is by overlap, so a session already under way comes back too; it isn't upcoming
                .Where(e => e.Start >= now)
                .OrderBy(e => e.Start)
                .Select(e => new UpcomingBookingModel(e.Id, Title(e.Summary), e.Start, e.End, e.Tentative))
                .ToList();
            return new UpcomingBookingsModel(true, bookings);
        }

        private static string Title(string summary) =>
            summary.StartsWith(CreateBookingCommandHandler.PendingPrefix, StringComparison.OrdinalIgnoreCase)
                ? summary[CreateBookingCommandHandler.PendingPrefix.Length..]
                : summary;
    }
}
