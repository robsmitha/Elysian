namespace Elysian.Application.Features.GoogleCalendar.Models
{
    /// <summary>
    /// Admin view of the Google Calendar connection. Never includes tokens.
    /// </summary>
    /// <param name="AccountId">Calendar id the token works against (the account email for "primary")</param>
    /// <param name="Error">Why booking is paused, when the connection needs attention</param>
    public record GoogleCalendarConnectionModel(
        bool Connected,
        string? AccountId,
        DateTimeOffset? ConnectedUtc,
        DateTimeOffset? LastValidatedUtc,
        string? Error);
}
