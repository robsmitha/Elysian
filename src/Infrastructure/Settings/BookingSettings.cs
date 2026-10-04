namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "Booking" configuration section. Every time of day is wall-clock time in <see cref="TimeZone"/>.
    /// </summary>
    public class BookingSettings
    {
        public const string SectionName = "Booking";

        /// <summary>
        /// IANA time zone the photographer works in. Clients see times converted to their own browser's zone.
        /// </summary>
        public string TimeZone { get; set; } = "America/New_York";

        /// <summary>
        /// Ranges a session may start in, inclusive of both ends, e.g. "07:30"-"10:30"
        /// </summary>
        public List<StartWindow> StartWindows { get; set; } =
        [
            new() { Start = "07:30", End = "10:30" },
            new() { Start = "16:00", End = "18:00" },
        ];

        /// <summary>
        /// Every session must finish by this time, e.g. "20:00"
        /// </summary>
        public string LatestEnd { get; set; } = "20:00";

        public int SlotIncrementMinutes { get; set; } = 30;

        /// <summary>
        /// Gap kept clear before and after each session. Only matters once <see cref="MaxEventsPerDay"/> is above zero.
        /// </summary>
        public int BufferMinutes { get; set; }

        /// <summary>
        /// Calendar events a day can already have and still take a booking (as long as nothing overlaps).
        /// 0 means any event at all (a session, a birthday, an all-day reminder) closes the day.
        /// </summary>
        public int MaxEventsPerDay { get; set; }

        /// <summary>
        /// Earliest bookable day is today plus this many days (e.g. 7 = a week out)
        /// </summary>
        public int MinimumNoticeDays { get; set; } = 7;

        /// <summary>
        /// Latest bookable day is today plus this many months
        /// </summary>
        public int HorizonMonths { get; set; } = 3;

        /// <summary>
        /// Inbox told about new bookings. Falls back to Resend:ToEmailAddress.
        /// </summary>
        public string? NotificationEmailAddress { get; set; }

        /// <summary>
        /// Sender for booking emails; must be on a domain verified in Resend. Falls back to Resend:FromEmailAddress.
        /// </summary>
        public string? FromEmailAddress { get; set; }

        /// <summary>
        /// Shown to clients in emails as where to reach the photographer
        /// </summary>
        public string? ContactEmailAddress { get; set; }

        public class StartWindow
        {
            public string Start { get; set; } = string.Empty;
            public string End { get; set; } = string.Empty;
        }
    }
}
