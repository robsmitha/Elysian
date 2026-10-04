namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "GoogleCalendar" configuration section (an OAuth Web client in Google Cloud with the Calendar API enabled).
    /// The refresh token is per tenant and lives in the OAuthToken table, set through ConnectGoogleCalendarCommand.
    /// </summary>
    public class GoogleCalendarSettings
    {
        public const string SectionName = "GoogleCalendar";

        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Secret: never log it
        /// </summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Calendar checked for availability and booked into. "primary" is the signed-in account's main calendar.
        /// </summary>
        public string CalendarId { get; set; } = "primary";
    }
}
