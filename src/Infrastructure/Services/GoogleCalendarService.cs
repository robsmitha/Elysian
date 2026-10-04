using System.Globalization;
using System.Net;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Google.Apis.Calendar.v3 against GoogleCalendar:CalendarId. A rejected access token is refreshed and retried once.
    /// </summary>
    public class GoogleCalendarService(IGoogleAccessTokenProvider tokenProvider, IOptions<GoogleCalendarSettings> options) : IGoogleCalendarService
    {
        private const string ApplicationName = "Elysian";

        private string CalendarId => string.IsNullOrWhiteSpace(options.Value.CalendarId) ? "primary" : options.Value.CalendarId;

        public Task<List<CalendarEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            ExecuteAsync(async service =>
            {
                var events = new List<CalendarEvent>();
                string? pageToken = null;
                do
                {
                    var request = service.Events.List(CalendarId);
                    request.TimeMinDateTimeOffset = from;
                    request.TimeMaxDateTimeOffset = to;
                    request.SingleEvents = true;
                    request.ShowDeleted = false;
                    request.MaxResults = 2500;
                    request.PageToken = pageToken;
                    request.Fields = "nextPageToken,items(status,start,end,attendees(self,responseStatus))";

                    var page = await request.ExecuteAsync(cancellationToken);
                    foreach (var item in page.Items ?? [])
                    {
                        if (item.Status == "cancelled" || item.Attendees?.Any(a => a.Self == true && a.ResponseStatus == "declined") == true)
                        {
                            continue;
                        }

                        if (ToCalendarEvent(item) is CalendarEvent calendarEvent)
                        {
                            events.Add(calendarEvent);
                        }
                    }
                    pageToken = page.NextPageToken;
                }
                while (pageToken != null);

                return events;
            }, cancellationToken);

        public Task<bool> CreateEventAsync(NewCalendarEvent calendarEvent, CancellationToken cancellationToken = default) =>
            ExecuteAsync(async service =>
            {
                var body = new Event
                {
                    Id = calendarEvent.Id,
                    Summary = calendarEvent.Summary,
                    Description = calendarEvent.Description,
                    Start = new EventDateTime { DateTimeDateTimeOffset = calendarEvent.Start, TimeZone = calendarEvent.TimeZone },
                    End = new EventDateTime { DateTimeDateTimeOffset = calendarEvent.End, TimeZone = calendarEvent.TimeZone },
                    Status = calendarEvent.Tentative ? "tentative" : "confirmed",
                    ExtendedProperties = new Event.ExtendedPropertiesData { Private__ = calendarEvent.PrivateProperties },
                };

                try
                {
                    await service.Events.Insert(body, CalendarId).ExecuteAsync(cancellationToken);
                    return true;
                }
                catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Conflict)
                {
                    // The id is derived from the booking, so this is the same booking submitted twice
                    return false;
                }
            }, cancellationToken);

        public Task<string> VerifyAccessAsync(CancellationToken cancellationToken = default) =>
            ExecuteAsync(service => GetCalendarIdAsync(service, cancellationToken), cancellationToken);

        public async Task<string> VerifyAccessAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            using var service = CreateService(accessToken);
            try
            {
                return await GetCalendarIdAsync(service, cancellationToken);
            }
            catch (GoogleApiException ex) when (IsAuthFailure(ex))
            {
                throw Forbidden(ex);
            }
        }

        private async Task<string> GetCalendarIdAsync(CalendarService service, CancellationToken cancellationToken)
        {
            var request = service.Calendars.Get(CalendarId);
            request.Fields = "id";
            var calendar = await request.ExecuteAsync(cancellationToken);
            return calendar.Id;
        }

        private async Task<T> ExecuteAsync<T>(Func<CalendarService, Task<T>> action, CancellationToken cancellationToken)
        {
            var accessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken: cancellationToken);
            try
            {
                using var service = CreateService(accessToken);
                return await action(service);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
            {
                // The cached access token was revoked early; a refresh also proves whether the refresh token still works
                accessToken = await tokenProvider.GetAccessTokenAsync(forceRefresh: true, cancellationToken);
            }

            try
            {
                using var service = CreateService(accessToken);
                return await action(service);
            }
            catch (GoogleApiException ex) when (IsAuthFailure(ex))
            {
                throw Forbidden(ex);
            }
        }

        private static CalendarService CreateService(string accessToken) => new(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
            ApplicationName = ApplicationName,
        });

        private static bool IsAuthFailure(GoogleApiException ex) =>
            ex.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            && ex.Error?.Errors?.Any(e => e.Reason is "rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded") != true;

        private CalendarAuthorizationException Forbidden(GoogleApiException ex) =>
            new(CalendarAuthorizationFailure.Forbidden,
                $"Google refused access to calendar \"{CalendarId}\" ({(int)ex.HttpStatusCode}). Check GoogleCalendar:CalendarId and that the "
                + "token was granted the Google Calendar scope, then reconnect on the admin Calendar page.", ex);

        private static CalendarEvent? ToCalendarEvent(Event item)
        {
            if (item.Start?.DateTimeDateTimeOffset is DateTimeOffset start && item.End?.DateTimeDateTimeOffset is DateTimeOffset end)
            {
                return CalendarEvent.Timed(start, end);
            }

            if (TryParseDate(item.Start?.Date, out var startDate))
            {
                return CalendarEvent.AllDay(startDate, TryParseDate(item.End?.Date, out var endDate) ? endDate : startDate.AddDays(1));
            }

            return null;
        }

        private static bool TryParseDate(string? value, out DateOnly date) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
