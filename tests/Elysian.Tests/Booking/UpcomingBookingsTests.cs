using Elysian.Application.Exceptions;
using Elysian.Application.Features.Booking.Commands;
using Elysian.Application.Features.Booking.Models;
using Elysian.Application.Features.Booking.Queries;
using Elysian.Application.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Elysian.Tests.Booking
{
    public class UpcomingBookingsTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<IGoogleCalendarService> calendar = new();

        [Fact]
        public async Task Lists_what_online_booking_created_soonest_first_without_the_pending_marker()
        {
            Returns(
                Event("b", "Family Session – Castillo", Now.AddDays(9), tentative: false),
                Event("a", $"{CreateBookingCommandHandler.PendingPrefix}Mini Session – Reyes", Now.AddDays(2), tentative: true));

            var result = await Ask(new GetUpcomingBookingsQuery());

            Assert.True(result.Available);
            Assert.Equal(["a", "b"], result.Bookings.Select(b => b.Id));
            Assert.Equal("Mini Session – Reyes", result.Bookings[0].Title);
            Assert.True(result.Bookings[0].Tentative);
            Assert.Equal("Family Session – Castillo", result.Bookings[1].Title);
            Assert.False(result.Bookings[1].Tentative);
        }

        [Fact]
        public async Task Asks_only_for_events_marked_as_bookings_within_the_days_asked_for()
        {
            Returns();

            await Ask(new GetUpcomingBookingsQuery(14));

            calendar.Verify(c => c.GetTaggedEventsAsync(Now, Now.AddDays(14), CreateBookingCommandHandler.SourceProperty, "true",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-5, 1)]
        [InlineData(5000, GetUpcomingBookingsQueryHandler.MaxDays)]
        public async Task Keeps_the_range_within_bounds(int asked, int used)
        {
            Returns();

            await Ask(new GetUpcomingBookingsQuery(asked));

            calendar.Verify(c => c.GetTaggedEventsAsync(Now, Now.AddDays(used), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Leaves_out_a_session_that_has_already_started()
        {
            Returns(Event("started", "Family Session – Castillo", Now.AddMinutes(-20), tentative: false));

            var result = await Ask(new GetUpcomingBookingsQuery());

            Assert.True(result.Available);
            Assert.Empty(result.Bookings);
        }

        [Fact]
        public async Task Says_the_list_is_unavailable_when_the_calendar_is_not_connected()
        {
            Throws(new CalendarAuthorizationException(CalendarAuthorizationFailure.Forbidden, "no access"));

            var result = await Ask(new GetUpcomingBookingsQuery());

            Assert.False(result.Available);
            Assert.Empty(result.Bookings);
        }

        [Fact]
        public async Task Says_the_list_is_unavailable_when_Google_cannot_be_reached()
        {
            Throws(new HttpRequestException("timed out"));

            var result = await Ask(new GetUpcomingBookingsQuery());

            Assert.False(result.Available);
        }

        private static TaggedCalendarEvent Event(string id, string summary, DateTimeOffset start, bool tentative) =>
            new(id, summary, start, start.AddHours(1), tentative);

        private void Returns(params TaggedCalendarEvent[] events) =>
            calendar.Setup(c => c.GetTaggedEventsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([.. events]);

        private void Throws(Exception exception) =>
            calendar.Setup(c => c.GetTaggedEventsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(exception);

        private Task<UpcomingBookingsModel> Ask(GetUpcomingBookingsQuery query) =>
            new GetUpcomingBookingsQueryHandler(calendar.Object, new FixedClock(), NullLogger<GetUpcomingBookingsQueryHandler>.Instance)
                .Handle(query, default);

        private sealed class FixedClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
