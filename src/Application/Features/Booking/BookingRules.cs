using System.Globalization;
using Elysian.Infrastructure.Settings;

namespace Elysian.Application.Features.Booking
{
    /// <summary>
    /// <see cref="BookingSettings"/> parsed and checked once, so a bad setting fails loudly instead of quietly offering no times
    /// </summary>
    public sealed record BookingRules(
        TimeZoneInfo TimeZone,
        IReadOnlyList<(TimeOnly Start, TimeOnly End)> StartWindows,
        TimeOnly LatestEnd,
        TimeSpan SlotIncrement,
        TimeSpan Buffer,
        int MaxEventsPerDay,
        int MinimumNoticeDays,
        int HorizonMonths)
    {
        public static BookingRules FromSettings(BookingSettings settings)
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);

            var windows = settings.StartWindows
                .Select(w => (Start: ParseTime(w.Start, "StartWindows:Start"), End: ParseTime(w.End, "StartWindows:End")))
                .OrderBy(w => w.Start)
                .ToList();
            if (windows.Count == 0 || windows.Any(w => w.End < w.Start))
            {
                throw new InvalidOperationException($"{BookingSettings.SectionName}:StartWindows must list at least one range with Start at or before End.");
            }

            if (settings.SlotIncrementMinutes <= 0)
            {
                throw new InvalidOperationException($"{BookingSettings.SectionName}:SlotIncrementMinutes must be positive.");
            }

            return new BookingRules(
                timeZone,
                windows,
                ParseTime(settings.LatestEnd, nameof(BookingSettings.LatestEnd)),
                TimeSpan.FromMinutes(settings.SlotIncrementMinutes),
                TimeSpan.FromMinutes(Math.Max(0, settings.BufferMinutes)),
                Math.Max(0, settings.MaxEventsPerDay),
                Math.Max(0, settings.MinimumNoticeDays),
                Math.Max(0, settings.HorizonMonths));
        }

        /// <summary>
        /// Today in the photographer's time zone
        /// </summary>
        public DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZone).DateTime);

        public DateOnly FirstBookableDate(DateTimeOffset now) => Today(now).AddDays(MinimumNoticeDays);

        public DateOnly LastBookableDate(DateTimeOffset now) => Today(now).AddMonths(HorizonMonths);

        /// <summary>
        /// The instant a wall-clock time on a date happens in the photographer's time zone. Times skipped by a
        /// daylight-saving jump return null; repeated times (fall back) resolve to the first occurrence.
        /// </summary>
        public DateTimeOffset? ToInstant(DateOnly date, TimeOnly time)
        {
            var local = date.ToDateTime(time, DateTimeKind.Unspecified);
            if (TimeZone.IsInvalidTime(local))
            {
                return null;
            }

            var offset = TimeZone.IsAmbiguousTime(local)
                ? TimeZone.GetAmbiguousTimeOffsets(local).Max()
                : TimeZone.GetUtcOffset(local);
            return new DateTimeOffset(local, offset);
        }

        /// <summary>
        /// Start of the day in the photographer's time zone (midnight, or the first valid moment after it)
        /// </summary>
        public DateTimeOffset StartOfDay(DateOnly date)
        {
            for (var minutes = 0; minutes < 24 * 60; minutes += 15)
            {
                if (ToInstant(date, TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes))) is DateTimeOffset start)
                {
                    return start;
                }
            }
            throw new InvalidOperationException($"{date} has no valid time in {TimeZone.Id}.");
        }

        private static TimeOnly ParseTime(string value, string name) =>
            TimeOnly.TryParseExact(value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? time
                : throw new InvalidOperationException($"{BookingSettings.SectionName}:{name} must be a time like \"07:30\", not \"{value}\".");
    }
}
