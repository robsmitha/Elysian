namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// The tenant's Google Calendar (GoogleCalendar:CalendarId), authorized with the stored refresh token.
    /// Throws <see cref="Exceptions.CalendarAuthorizationException"/> when the token is missing, revoked or expired.
    /// </summary>
    public interface IGoogleCalendarService
    {
        /// <summary>
        /// Every event overlapping the range, recurring events expanded, skipping cancelled events and invitations the owner declined.
        /// Events marked "free" are included: anything on the calendar counts.
        /// </summary>
        Task<List<CalendarEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

        /// <summary>
        /// The timed events overlapping the range that carry the given private property, soonest first, skipping cancelled
        /// ones. This is how the events online booking created are listed without reading the photographer's own.
        /// </summary>
        Task<List<TaggedCalendarEvent>> GetTaggedEventsAsync(DateTimeOffset from, DateTimeOffset to, string propertyName,
            string propertyValue, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the event with the given id. Returns false when an event with that id already exists (a repeated submission).
        /// </summary>
        Task<bool> CreateEventAsync(NewCalendarEvent calendarEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lightweight call proving the token can read the calendar's events; returns the calendar's name (the account
        /// email for a main calendar that hasn't been renamed)
        /// </summary>
        Task<string> VerifyAccessAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Same as <see cref="VerifyAccessAsync(CancellationToken)"/>, with a specific access token (one not yet stored)
        /// </summary>
        Task<string> VerifyAccessAsync(string accessToken, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An event as a time range. All-day events carry dates instead (end exclusive), since they cover whole days
    /// wherever the calendar is viewed; <see cref="Features.Booking.SlotCalculator"/> places them in the photographer's time zone.
    /// </summary>
    /// <param name="IsBooking">A session booked through the site, as opposed to anything else on the calendar</param>
    /// <param name="IsFree">Marked as not taking up the owner's time ("Free" in Google Calendar, its default for all-day events)</param>
    public record CalendarEvent(DateTimeOffset? Start, DateTimeOffset? End, DateOnly? AllDayStart, DateOnly? AllDayEnd,
        bool IsBooking = false, bool IsFree = false)
    {
        public static CalendarEvent Timed(DateTimeOffset start, DateTimeOffset end) => new(start, end, null, null);

        public static CalendarEvent AllDay(DateOnly start, DateOnly endExclusive) => new(null, null, start, endExclusive);
    }

    /// <summary>
    /// An event found by a private property it carries, with what is needed to list it
    /// </summary>
    /// <param name="Tentative">Not yet confirmed by the photographer</param>
    public record TaggedCalendarEvent(string Id, string Summary, DateTimeOffset Start, DateTimeOffset End, bool Tentative);

    /// <param name="Id">Google event id: 5-1024 characters of a-v and 0-9</param>
    /// <param name="Tentative">Shown as tentative until the photographer confirms it</param>
    /// <param name="PrivateProperties">Hidden metadata identifying the booking</param>
    public record NewCalendarEvent(string Id, string Summary, string Description, DateTimeOffset Start, DateTimeOffset End,
        string TimeZone, bool Tentative, IDictionary<string, string> PrivateProperties);
}
