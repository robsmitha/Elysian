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
        /// Creates the event with the given id. Returns false when an event with that id already exists (a repeated submission).
        /// </summary>
        Task<bool> CreateEventAsync(NewCalendarEvent calendarEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lightweight call proving the token can read the calendar; returns the calendar's id (the account email for "primary")
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
    public record CalendarEvent(DateTimeOffset? Start, DateTimeOffset? End, DateOnly? AllDayStart, DateOnly? AllDayEnd)
    {
        public static CalendarEvent Timed(DateTimeOffset start, DateTimeOffset end) => new(start, end, null, null);

        public static CalendarEvent AllDay(DateOnly start, DateOnly endExclusive) => new(null, null, start, endExclusive);
    }

    /// <param name="Id">Google event id: 5-1024 characters of a-v and 0-9</param>
    /// <param name="Tentative">Shown as tentative until the photographer confirms it</param>
    /// <param name="PrivateProperties">Hidden metadata identifying the booking</param>
    public record NewCalendarEvent(string Id, string Summary, string Description, DateTimeOffset Start, DateTimeOffset End,
        string TimeZone, bool Tentative, IDictionary<string, string> PrivateProperties);
}
