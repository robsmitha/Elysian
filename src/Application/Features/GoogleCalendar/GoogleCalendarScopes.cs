namespace Elysian.Application.Features.GoogleCalendar
{
    /// <summary>
    /// What a photographer is asked to allow. Events only: reading every event is how availability is worked out
    /// (an all-day event is "free" by default, so the narrower availability-only scopes would miss it), and writing
    /// events is how a booking is stored. No access to calendar settings, sharing, or deleting calendars.
    /// </summary>
    public static class GoogleCalendarScopes
    {
        public const string Events = "https://www.googleapis.com/auth/calendar.events";

        /// <summary>
        /// Everything in Google Calendar. Connections made before the Connect button have this, and it still works.
        /// </summary>
        public const string FullCalendar = "https://www.googleapis.com/auth/calendar";

        /// <summary>
        /// Asked for at sign-in. The address is only used to show which account is connected.
        /// </summary>
        public static readonly string[] Requested = ["openid", "email", Events];

        /// <summary>
        /// True when the granted scopes (space separated, as Google returns them) are enough for booking
        /// </summary>
        public static bool AllowsBooking(string? granted) =>
            (granted ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(s => s is Events or FullCalendar);
    }
}
