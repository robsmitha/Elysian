using Elysian.Application.Interfaces;

namespace Elysian.Application.Features.Booking
{
    /// <summary>
    /// Reads the calendar and runs <see cref="SlotCalculator"/>, so showing times and accepting a booking agree exactly
    /// </summary>
    public class BookingAvailabilityService(IGoogleCalendarService calendarService, BookingRules rules, TimeProvider timeProvider)
    {
        public BookingRules Rules => rules;

        /// <summary>
        /// Start times per photographer date between <paramref name="from"/> and <paramref name="to"/> (inclusive).
        /// Skips the calendar entirely when the range is outside the bookable window.
        /// </summary>
        public async Task<SortedDictionary<DateOnly, List<DateTimeOffset>>> GetSlotsAsync(int durationMinutes, DateOnly from, DateOnly to,
            CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();
            var first = from > rules.FirstBookableDate(now) ? from : rules.FirstBookableDate(now);
            var last = to < rules.LastBookableDate(now) ? to : rules.LastBookableDate(now);
            if (first > last)
            {
                return [];
            }

            var (rangeStart, rangeEnd) = SlotCalculator.EventRange(rules, first, last);
            var events = await calendarService.GetEventsAsync(rangeStart, rangeEnd, cancellationToken);

            return SlotCalculator.GetSlots(rules, TimeSpan.FromMinutes(durationMinutes), first, last, events, now);
        }
    }
}
