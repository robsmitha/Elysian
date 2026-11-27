using System.Globalization;
using Elysian.Infrastructure.Settings;

namespace Elysian.Application.Features.Booking
{
    /// <summary>
    /// How the values in <see cref="BookingSettings"/> are written: days by name, times as "07:30". One place,
    /// so what the settings page accepts and what the rules can read are the same thing.
    /// </summary>
    public static class BookingDays
    {
        public static DayOfWeek? Find(string? name) =>
            Enum.GetValues<DayOfWeek>().Cast<DayOfWeek?>().FirstOrDefault(d => string.Equals(d.ToString(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static BookingSettings.BookingOtherEvents? FindOtherEvents(string? name) =>
            Enum.GetValues<BookingSettings.BookingOtherEvents>().Cast<BookingSettings.BookingOtherEvents?>()
                .FirstOrDefault(d => string.Equals(d.ToString(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static TimeOnly? FindTime(string? value) =>
            TimeOnly.TryParseExact(value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

        public static string Write(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <see cref="BookingSettings"/> parsed and checked once, so a bad setting fails loudly instead of quietly offering no times
    /// </summary>
    public sealed record BookingRules(
        TimeZoneInfo TimeZone,
        IReadOnlyList<(TimeOnly Start, TimeOnly End)> StartWindows,
        TimeOnly LatestEnd,
        TimeSpan SlotIncrement,
        TimeSpan Buffer,
        IReadOnlySet<DayOfWeek> Days,
        int MaxSessionsPerDay,
        BookingSettings.BookingOtherEvents OtherEvents,
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

            var days = new HashSet<DayOfWeek>();
            foreach (var name in settings.Days ?? [])
            {
                days.Add(BookingDays.Find(name) ?? throw new InvalidOperationException(
                    $"{BookingSettings.SectionName}:Days must be names of days like \"Saturday\", not \"{name}\"."));
            }
            if (days.Count == 0)
            {
                days.UnionWith(Enum.GetValues<DayOfWeek>());
            }

            var otherEvents = BookingDays.FindOtherEvents(settings.OtherEvents) ?? throw new InvalidOperationException(
                $"{BookingSettings.SectionName}:OtherEvents must be CloseDay or BlockTime, not \"{settings.OtherEvents}\".");

            return new BookingRules(
                timeZone,
                windows,
                ParseTime(settings.LatestEnd, nameof(BookingSettings.LatestEnd)),
                TimeSpan.FromMinutes(settings.SlotIncrementMinutes),
                TimeSpan.FromMinutes(Math.Max(0, settings.BufferMinutes)),
                days,
                Math.Max(0, settings.MaxSessionsPerDay),
                otherEvents,
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
            BookingDays.FindTime(value) is TimeOnly time
                ? time
                : throw new InvalidOperationException($"{BookingSettings.SectionName}:{name} must be a time like \"07:30\", not \"{value}\".");
    }
}
