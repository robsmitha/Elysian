namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "GoogleCalendar" configuration section (an OAuth Web client in Google Cloud with the Calendar API enabled).
    /// The refresh token is per tenant and lives in the OAuthToken table, stored when the photographer connects
    /// their calendar (CompleteGoogleCalendarConnectionCommand).
    /// </summary>
    public class GoogleCalendarSettings
    {
        public const string SectionName = "GoogleCalendar";

        [PlatformOnly]
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Secret: never log it
        /// </summary>
        [PlatformOnly]
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Where Google sends the photographer back after they allow access: one page in the admin app for every
        /// tenant. It must match an authorised redirect URI on the OAuth client exactly.
        /// </summary>
        [PlatformOnly]
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>
        /// Calendar checked for availability and booked into. "primary" is the signed-in account's main calendar.
        /// </summary>
        public string CalendarId { get; set; } = "primary";
    }
}
