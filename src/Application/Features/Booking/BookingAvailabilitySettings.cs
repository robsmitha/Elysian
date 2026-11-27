using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;

namespace Elysian.Application.Features.Booking
{
    /// <param name="Start">"07:30"</param>
    /// <param name="End">The last time a session may start in this range, "10:30"</param>
    public sealed record BookingStartRange(string? Start, string? End);

    /// <summary>
    /// When a site can be booked, as its owner sets it. Times are wall-clock times in <paramref name="TimeZone"/>.
    /// </summary>
    /// <param name="Days">The days of the week sessions are offered on, by English name</param>
    /// <param name="MaxSessionsPerDay">0 for no limit</param>
    /// <param name="OtherEvents">"CloseDay" or "BlockTime": what anything else on the calendar does</param>
    /// <param name="TimeZones">The zones an owner can choose from</param>
    public sealed record BookingAvailabilitySettingsModel(
        IReadOnlyList<string> Days,
        IReadOnlyList<BookingStartRange> StartRanges,
        string LatestEnd,
        int SlotIncrementMinutes,
        int MaxSessionsPerDay,
        int BufferMinutes,
        string OtherEvents,
        int MinimumNoticeDays,
        int HorizonMonths,
        string TimeZone,
        IReadOnlyList<BookingTimeZoneOption> TimeZones)
    {
        public static BookingAvailabilitySettingsModel From(BookingSettings settings)
        {
            var rules = BookingRules.FromSettings(settings);
            return new BookingAvailabilitySettingsModel(
                [.. Enum.GetValues<DayOfWeek>().Where(rules.Days.Contains).Select(d => d.ToString())],
                [.. rules.StartWindows.Select(w => new BookingStartRange(BookingDays.Write(w.Start), BookingDays.Write(w.End)))],
                BookingDays.Write(rules.LatestEnd),
                (int)rules.SlotIncrement.TotalMinutes,
                rules.MaxSessionsPerDay,
                (int)rules.Buffer.TotalMinutes,
                rules.OtherEvents.ToString(),
                rules.MinimumNoticeDays,
                rules.HorizonMonths,
                settings.TimeZone,
                BookingTimeZones.All);
        }
    }

    /// <summary>
    /// What an owner may set, and its limits. The admin screen offers the same choices; these are the check.
    /// </summary>
    public static class BookingAvailabilityLimits
    {
        public const int MaxStartRanges = 4;
        public const int MaxSessionsPerDay = 4;
        public const int MaxBufferMinutes = 240;
        public const int MaxNoticeDays = 60;
        public const int MaxHorizonMonths = 12;
        public static readonly IReadOnlyList<int> SlotIncrements = [15, 30, 60];
    }

    /// <summary>
    /// The current site's booking hours and limits
    /// </summary>
    [Authorize(Policy = PolicyNames.ProductRead)]
    public record GetBookingAvailabilitySettingsQuery : IRequest<BookingAvailabilitySettingsModel>;

    public class GetBookingAvailabilitySettingsQueryHandler(ITenantSettings<BookingSettings> settings)
        : IRequestHandler<GetBookingAvailabilitySettingsQuery, BookingAvailabilitySettingsModel>
    {
        public Task<BookingAvailabilitySettingsModel> Handle(GetBookingAvailabilitySettingsQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(BookingAvailabilitySettingsModel.From(settings.Value));
    }

    /// <summary>
    /// Changes when the site can be booked. Only these values of the site's booking settings are touched; its
    /// email addresses and anything else it has set stay as they are. Bookings already made keep their times.
    /// </summary>
    [Authorize(Policy = PolicyNames.ProductWrite)]
    public record SaveBookingAvailabilitySettingsCommand(
        IReadOnlyList<string?>? Days,
        IReadOnlyList<BookingStartRange?>? StartRanges,
        string? LatestEnd,
        int SlotIncrementMinutes,
        int MaxSessionsPerDay,
        int BufferMinutes,
        string? OtherEvents,
        int MinimumNoticeDays,
        int HorizonMonths,
        string? TimeZone) : IRequest<BookingAvailabilitySettingsModel>;

    public class SaveBookingAvailabilitySettingsCommandHandler(ElysianContext context, IMemoryCache cache,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor, ITenantSettings<BookingSettings> settings)
        : IRequestHandler<SaveBookingAvailabilitySettingsCommand, BookingAvailabilitySettingsModel>
    {
        public async Task<BookingAvailabilitySettingsModel> Handle(SaveBookingAvailabilitySettingsCommand request, CancellationToken cancellationToken)
        {
            var values = Check(request);

            await BookingSettingsWriter.WriteAsync(context, cache, multiTenantContextAccessor, values,
                "This site's booking settings can't be read, so nothing was changed.", cancellationToken);

            // What the site reads from now on: its other settings, with these laid over them
            var saved = TenantSettingsSection.Overlay(settings.Value, new JObject(values.Select(v => new JProperty(v.Key, v.Value))).ToString(), BookingSettings.SectionName);
            return BookingAvailabilitySettingsModel.From(saved);
        }

        /// <summary>
        /// The values to store, by setting name, or a validation error naming every field that is wrong
        /// </summary>
        public static Dictionary<string, JToken> Check(SaveBookingAvailabilitySettingsCommand request)
        {
            var failures = new List<ValidationFailure>();
            void Fail(string field, string message) => failures.Add(new ValidationFailure(field, message));

            // ---- Days
            var days = new SortedSet<DayOfWeek>();
            foreach (var name in request.Days ?? [])
            {
                if (BookingDays.Find(name) is DayOfWeek day)
                {
                    days.Add(day);
                }
                else
                {
                    Fail(nameof(request.Days), "One of the days isn't a day of the week.");
                    break;
                }
            }
            if (days.Count == 0 && failures.Count == 0)
            {
                Fail(nameof(request.Days), "Choose at least one day, or nobody can book.");
            }

            // ---- Hours
            var ranges = new List<(TimeOnly Start, TimeOnly End)>();
            var given = request.StartRanges ?? [];
            if (given.Count == 0)
            {
                Fail(nameof(request.StartRanges), "Add at least one range of hours a session can start in.");
            }
            else if (given.Count > BookingAvailabilityLimits.MaxStartRanges)
            {
                Fail(nameof(request.StartRanges), $"There can be up to {BookingAvailabilityLimits.MaxStartRanges} ranges of hours.");
            }
            else
            {
                foreach (var range in given)
                {
                    if (QuarterHour(range?.Start) is not TimeOnly start || QuarterHour(range?.End) is not TimeOnly end)
                    {
                        Fail(nameof(request.StartRanges), "Each range needs a start and an end on the quarter hour.");
                        break;
                    }
                    if (end < start)
                    {
                        Fail(nameof(request.StartRanges), "A range can't end before it starts.");
                        break;
                    }
                    ranges.Add((start, end));
                }

                ranges.Sort();
                if (ranges.Count == given.Count && ranges.Zip(ranges.Skip(1)).Any(pair => pair.Second.Start <= pair.First.End))
                {
                    Fail(nameof(request.StartRanges), "Two of the ranges overlap. Join them into one, or move them apart.");
                }
            }

            var latestEnd = QuarterHour(request.LatestEnd);
            if (latestEnd == null)
            {
                Fail(nameof(request.LatestEnd), "Choose the time every session finishes by.");
            }
            else if (ranges.Count > 0 && latestEnd <= ranges[0].Start)
            {
                Fail(nameof(request.LatestEnd), "Sessions have to finish after the first one can start.");
            }

            if (!BookingAvailabilityLimits.SlotIncrements.Contains(request.SlotIncrementMinutes))
            {
                Fail(nameof(request.SlotIncrementMinutes), "Start times can be offered every 15, 30 or 60 minutes.");
            }

            // ---- How many
            if (request.MaxSessionsPerDay is < 0 or > BookingAvailabilityLimits.MaxSessionsPerDay)
            {
                Fail(nameof(request.MaxSessionsPerDay), $"A day can have up to {BookingAvailabilityLimits.MaxSessionsPerDay} sessions, or no limit.");
            }
            if (request.BufferMinutes is < 0 or > BookingAvailabilityLimits.MaxBufferMinutes || request.BufferMinutes % 15 != 0)
            {
                Fail(nameof(request.BufferMinutes), "The time kept clear around a session can be up to 4 hours, in quarter hours.");
            }
            var otherEvents = BookingDays.FindOtherEvents(request.OtherEvents);
            if (otherEvents == null)
            {
                Fail(nameof(request.OtherEvents), "Choose what other things on your calendar do.");
            }

            // ---- How far ahead
            if (request.MinimumNoticeDays is < 0 or > BookingAvailabilityLimits.MaxNoticeDays)
            {
                Fail(nameof(request.MinimumNoticeDays), $"The earliest booking can be up to {BookingAvailabilityLimits.MaxNoticeDays} days ahead.");
            }
            if (request.HorizonMonths is < 1 or > BookingAvailabilityLimits.MaxHorizonMonths)
            {
                Fail(nameof(request.HorizonMonths), $"The latest booking can be 1 to {BookingAvailabilityLimits.MaxHorizonMonths} months ahead.");
            }
            else if (request.MinimumNoticeDays > 27 * request.HorizonMonths)
            {
                Fail(nameof(request.MinimumNoticeDays), "The earliest booking has to come before the latest one.");
            }

            // The list is what the screen offers; the second check is that this machine knows the zone too
            var zone = BookingTimeZones.Find(request.TimeZone);
            if (zone == null || !TimeZoneInfo.TryFindSystemTimeZoneById(zone.Id, out _))
            {
                Fail(nameof(request.TimeZone), "Please choose a time zone from the list.");
            }

            if (failures.Count > 0)
            {
                throw new CustomValidationException(failures);
            }

            return new Dictionary<string, JToken>
            {
                [nameof(BookingSettings.Days)] = new JArray(days.Select(d => d.ToString())),
                [nameof(BookingSettings.StartWindows)] = new JArray(ranges.Select(r => new JObject
                {
                    [nameof(BookingSettings.StartWindow.Start)] = BookingDays.Write(r.Start),
                    [nameof(BookingSettings.StartWindow.End)] = BookingDays.Write(r.End),
                })),
                [nameof(BookingSettings.LatestEnd)] = BookingDays.Write(latestEnd!.Value),
                [nameof(BookingSettings.SlotIncrementMinutes)] = request.SlotIncrementMinutes,
                [nameof(BookingSettings.MaxSessionsPerDay)] = request.MaxSessionsPerDay,
                [nameof(BookingSettings.BufferMinutes)] = request.BufferMinutes,
                [nameof(BookingSettings.OtherEvents)] = otherEvents!.Value.ToString(),
                [nameof(BookingSettings.MinimumNoticeDays)] = request.MinimumNoticeDays,
                [nameof(BookingSettings.HorizonMonths)] = request.HorizonMonths,
                [nameof(BookingSettings.TimeZone)] = zone!.Id,
            };
        }

        private static TimeOnly? QuarterHour(string? value) =>
            BookingDays.FindTime(value?.Trim()) is TimeOnly time && time.Minute % 15 == 0 && time.Second == 0 ? time : null;
    }

    /// <summary>
    /// Writes values into the current site's own booking settings, leaving every other value it has alone
    /// </summary>
    public static class BookingSettingsWriter
    {
        /// <param name="unreadable">What to say when the stored settings aren't JSON</param>
        public static async Task WriteAsync(ElysianContext context, IMemoryCache cache,
            IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor, IReadOnlyDictionary<string, JToken> values,
            string unreadable, CancellationToken cancellationToken)
        {
            var row = await context.TenantSettings.FirstOrDefaultAsync(s => s.Section == BookingSettings.SectionName, cancellationToken);
            if (row == null)
            {
                row = new TenantSetting { Section = BookingSettings.SectionName, Json = "{}" };
                context.TenantSettings.Add(row);
            }

            JObject json;
            try
            {
                json = string.IsNullOrWhiteSpace(row.Json) ? [] : JObject.Parse(row.Json);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                throw new ConflictException(unreadable);
            }

            foreach (var (name, value) in values)
            {
                // Settings are read without regard to case, so "timeZone" and "TimeZone" are the same value
                foreach (var existing in json.Properties().Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    existing.Remove();
                }
                json[name] = value;
            }
            row.Json = json.ToString(Newtonsoft.Json.Formatting.None);

            await context.SaveChangesAsync(cancellationToken);

            // This instance reads the new values at once; others pick them up when their copy runs out
            var tenantId = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Id!;
            cache.Remove(TenantSettingsSection.CacheKey(tenantId, BookingSettings.SectionName));
        }
    }
}
