using Elysian.Application.Interfaces;

namespace Elysian.Application.Features.Booking
{
    /// <summary>
    /// Works out bookable start times from the rules and what's already on the calendar. Pure, so the same
    /// answer comes back when showing times and when re-checking a submitted time.
    /// </summary>
    public static class SlotCalculator
    {
        /// <summary>
        /// Start times per day (photographer's dates) between <paramref name="from"/> and <paramref name="to"/>, inclusive,
        /// clamped to the notice and horizon window. Days without a time are left out.
        /// </summary>
        public static SortedDictionary<DateOnly, List<DateTimeOffset>> GetSlots(BookingRules rules, TimeSpan duration,
            DateOnly from, DateOnly to, IReadOnlyCollection<CalendarEvent> events, DateTimeOffset now)
        {
            var result = new SortedDictionary<DateOnly, List<DateTimeOffset>>();
            if (duration <= TimeSpan.Zero)
            {
                return result;
            }

            var first = Max(from, rules.FirstBookableDate(now));
            var last = Min(to, rules.LastBookableDate(now));

            var periods = events.Select(e => ToPeriod(rules, e)).ToList();

            for (var date = first; date <= last; date = date.AddDays(1))
            {
                var slots = GetSlotsForDay(rules, duration, date, periods, now);
                if (slots.Count > 0)
                {
                    result[date] = slots;
                }
            }

            return result;
        }

        /// <summary>
        /// The calendar range to read so <see cref="GetSlots"/> sees every event that can affect those days
        /// </summary>
        public static (DateTimeOffset From, DateTimeOffset To) EventRange(BookingRules rules, DateOnly from, DateOnly to) =>
            (rules.StartOfDay(from), rules.StartOfDay(to.AddDays(1)));

        private static List<DateTimeOffset> GetSlotsForDay(BookingRules rules, TimeSpan duration, DateOnly date,
            List<(DateTimeOffset Start, DateTimeOffset End)> periods, DateTimeOffset now)
        {
            var slots = new List<DateTimeOffset>();

            var dayStart = rules.StartOfDay(date);
            var dayEnd = rules.StartOfDay(date.AddDays(1));
            var sameDay = periods.Where(p => p.Start < dayEnd && p.End > dayStart).ToList();

            // At the limit (0 by default: any event at all) the whole day is taken
            if (sameDay.Count > rules.MaxEventsPerDay)
            {
                return slots;
            }

            if (rules.ToInstant(date, rules.LatestEnd) is not DateTimeOffset latestEnd)
            {
                return slots;
            }

            foreach (var (windowStart, windowEnd) in rules.StartWindows)
            {
                // Stepped as an offset from the window start, since TimeOnly wraps at midnight
                for (var offset = TimeSpan.Zero; windowStart.ToTimeSpan() + offset <= windowEnd.ToTimeSpan(); offset += rules.SlotIncrement)
                {
                    if (rules.ToInstant(date, windowStart.Add(offset)) is DateTimeOffset start)
                    {
                        var end = start + duration;
                        var fits = start > now
                            && end <= latestEnd
                            && !sameDay.Any(p => start - rules.Buffer < p.End && end + rules.Buffer > p.Start);

                        if (fits && !slots.Contains(start))
                        {
                            slots.Add(start);
                        }
                    }
                }
            }

            slots.Sort();
            return slots;
        }

        private static (DateTimeOffset Start, DateTimeOffset End) ToPeriod(BookingRules rules, CalendarEvent e)
        {
            if (e.Start is DateTimeOffset start && e.End is DateTimeOffset end)
            {
                return (start, end);
            }

            var allDayStart = e.AllDayStart ?? throw new ArgumentException("Calendar event has neither times nor dates.", nameof(e));
            var allDayEnd = e.AllDayEnd ?? allDayStart.AddDays(1);
            return (rules.StartOfDay(allDayStart), rules.StartOfDay(allDayEnd));
        }

        private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

        private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
    }
}
