namespace Elysian.Application.Exceptions
{
    /// <summary>
    /// Google Calendar can't be used until the photographer (re)connects it on the admin Calendar page.
    /// Messages never include tokens or client secrets.
    /// </summary>
    public class CalendarAuthorizationException(CalendarAuthorizationFailure failure, string message, Exception? innerException = null)
        : Exception(message, innerException)
    {
        public const string ReconnectHelp = "Google Calendar access was revoked or has expired (invalid_grant). Online booking is paused "
            + "until it's reconnected: get a new refresh token for the calendar account and paste it on the admin Calendar page (/admin/calendar).";

        public CalendarAuthorizationFailure Failure { get; } = failure;
    }

    public enum CalendarAuthorizationFailure
    {
        /// <summary>
        /// GoogleCalendar:ClientId or ClientSecret isn't configured
        /// </summary>
        NotConfigured,

        /// <summary>
        /// No refresh token has been stored for the tenant yet
        /// </summary>
        NotConnected,

        /// <summary>
        /// Google rejected the refresh token (invalid_grant): revoked, expired, or the password changed
        /// </summary>
        Revoked,

        /// <summary>
        /// Google accepted the token but refused calendar access (e.g. a missing scope)
        /// </summary>
        Forbidden,
    }
}
