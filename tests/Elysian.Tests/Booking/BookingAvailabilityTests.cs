using Elysian.Application.Exceptions;
using Elysian.Application.Features.Booking;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.Booking
{
    public class SlotCalculatorTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 27, 17, 0, 0, TimeSpan.Zero);
        // A Saturday, two weeks out. Eastern time is five hours behind UTC in December.
        private static readonly DateOnly Saturday = new(2026, 12, 12);
        private static readonly TimeSpan Eastern = TimeSpan.FromHours(-5);

        private static DateTimeOffset At(DateOnly date, int hour, int minute = 0) => new(date.Year, date.Month, date.Day, hour, minute, 0, Eastern);

        private static CalendarEvent Booking(DateOnly date, int hour, int minutes = 60) =>
            CalendarEvent.Timed(At(date, hour), At(date, hour).AddMinutes(minutes)) with { IsBooking = true };

        private static List<string> Times(BookingSettings settings, DateOnly date, int minutes = 60, params CalendarEvent[] events)
        {
            var slots = SlotCalculator.GetSlots(BookingRules.FromSettings(settings), TimeSpan.FromMinutes(minutes), date, date, events, Now);
            return slots.TryGetValue(date, out var times) ? [.. times.Select(t => t.ToOffset(Eastern).ToString("H:mm"))] : [];
        }

        [Fact]
        public void A_site_that_has_set_nothing_offers_mornings_and_evenings_every_day()
        {
            var settings = new BookingSettings();

            Assert.Equal(["7:30", "8:00", "8:30", "9:00", "9:30", "10:00", "10:30", "16:00", "16:30", "17:00", "17:30", "18:00"], Times(settings, Saturday));
            for (var day = 0; day < 7; day++)
            {
                Assert.Equal(12, Times(settings, Saturday.AddDays(day)).Count);
            }
            // Finished by 8 PM: three hours can start at 5 at the latest
            Assert.Equal("17:00", Times(settings, Saturday, 180).Last());
            // A week's notice, three months out
            Assert.Empty(Times(settings, new DateOnly(2026, 12, 3)));
            Assert.NotEmpty(Times(settings, new DateOnly(2026, 12, 4)));
            Assert.NotEmpty(Times(settings, new DateOnly(2027, 2, 27)));
            Assert.Empty(Times(settings, new DateOnly(2027, 2, 28)));
        }

        [Fact]
        public void A_site_that_has_set_nothing_loses_the_day_to_anything_on_the_calendar()
        {
            var settings = new BookingSettings();

            Assert.Empty(Times(settings, Saturday, 60, Booking(Saturday, 8)));
            Assert.Empty(Times(settings, Saturday, 60, CalendarEvent.Timed(At(Saturday, 13), At(Saturday, 14))));
            Assert.Empty(Times(settings, Saturday, 60, CalendarEvent.AllDay(Saturday, Saturday.AddDays(1)) with { IsFree = true }));
            // The day before and the day after are untouched
            Assert.Equal(12, Times(settings, Saturday.AddDays(1), 60, CalendarEvent.AllDay(Saturday, Saturday.AddDays(1))).Count);
        }

        [Fact]
        public void Only_the_days_switched_on_are_offered()
        {
            var settings = new BookingSettings { Days = ["saturday", "Sunday"] };

            Assert.NotEmpty(Times(settings, Saturday));
            Assert.NotEmpty(Times(settings, Saturday.AddDays(1)));
            Assert.Empty(Times(settings, Saturday.AddDays(2)));
            Assert.Empty(Times(settings, Saturday.AddDays(-1)));
        }

        [Fact]
        public void A_day_takes_sessions_up_to_its_limit_around_the_ones_it_has()
        {
            var settings = new BookingSettings { MaxSessionsPerDay = 2 };

            // One booked at 8: its own hour is gone, the rest of the day is open
            Assert.Equal(["9:00", "9:30", "10:00", "10:30", "16:00", "16:30", "17:00", "17:30", "18:00"], Times(settings, Saturday, 60, Booking(Saturday, 8)));
            // Two booked: the day is full
            Assert.Empty(Times(settings, Saturday, 60, Booking(Saturday, 8), Booking(Saturday, 16)));
            // No limit at all
            settings.MaxSessionsPerDay = 0;
            Assert.Equal(["10:00", "10:30", "18:00"], Times(settings, Saturday, 60, Booking(Saturday, 8), Booking(Saturday, 9), Booking(Saturday, 16), Booking(Saturday, 17)));
        }

        [Fact]
        public void Time_is_kept_clear_around_a_session()
        {
            var settings = new BookingSettings { MaxSessionsPerDay = 2, BufferMinutes = 60 };

            // Booked 8 to 9: nothing may end after 7, or start before 10
            Assert.Equal(["10:00", "10:30", "16:00", "16:30", "17:00", "17:30", "18:00"], Times(settings, Saturday, 60, Booking(Saturday, 8)));
        }

        [Fact]
        public void Other_events_can_take_only_their_own_time()
        {
            var settings = new BookingSettings { OtherEvents = "BlockTime" };
            var dentist = CalendarEvent.Timed(At(Saturday, 9), At(Saturday, 10));

            Assert.Equal(["7:30", "8:00", "10:00", "10:30", "16:00", "16:30", "17:00", "17:30", "18:00"], Times(settings, Saturday, 60, dentist));
            // Marked Free, timed or all day: it takes nothing
            Assert.Equal(12, Times(settings, Saturday, 60, dentist with { IsFree = true }).Count);
            Assert.Equal(12, Times(settings, Saturday, 60, CalendarEvent.AllDay(Saturday, Saturday.AddDays(1)) with { IsFree = true }).Count);
            // An all-day event marked Busy is a day off
            Assert.Empty(Times(settings, Saturday, 60, CalendarEvent.AllDay(Saturday, Saturday.AddDays(1))));
            // It doesn't use up the day's one session, but a booking still does
            Assert.Empty(Times(settings, Saturday, 60, dentist, Booking(Saturday, 16)));
        }

        [Theory]
        [InlineData("Someday", "CloseDay")]
        [InlineData("6", "CloseDay")]
        [InlineData("Saturday", "Sometimes")]
        [InlineData("Saturday", "1")]
        public void Settings_that_cannot_be_read_fail_loudly(string day, string otherEvents)
        {
            Assert.Throws<InvalidOperationException>(() => BookingRules.FromSettings(new BookingSettings { Days = [day], OtherEvents = otherEvents }));
        }
    }

    public class BookingAvailabilitySettingsTests
    {
        private readonly TestHost host = new();
        private readonly MemoryCache cache = new(new MemoryCacheOptions());

        private static SaveBookingAvailabilitySettingsCommand Valid() => new(
            ["Saturday", "friday", "Sunday"], [new("16:00", "18:30"), new("8:00", "9:00")], "20:30", 15, 2, 30, "blocktime", 2, 6, "america/chicago");

        [Fact]
        public async Task A_site_that_has_set_nothing_reads_the_defaults()
        {
            var model = await new GetBookingAvailabilitySettingsQueryHandler(Settings()).Handle(new(), CancellationToken.None);

            Assert.Equal(["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"], model.Days);
            Assert.Equal([new("07:30", "10:30"), new("16:00", "18:00")], model.StartRanges);
            Assert.Equal(("20:00", 30, 1, 0, "CloseDay", 7, 3, "America/New_York"),
                (model.LatestEnd, model.SlotIncrementMinutes, model.MaxSessionsPerDay, model.BufferMinutes, model.OtherEvents, model.MinimumNoticeDays, model.HorizonMonths, model.TimeZone));
            Assert.Contains(model.TimeZones, z => z.Id == "America/Chicago");
        }

        [Fact]
        public async Task Saving_changes_what_the_site_reads_straight_away_and_leaves_its_other_settings()
        {
            host.Context.TenantSettings.Add(new TenantSetting { Section = "Booking", Json = """{"ContactEmailAddress":"studio@example.com","minimumNoticeDays":9,"maxEventsPerDay":3}""" });
            await host.Context.SaveChangesAsync();
            // Read once, so there is a copy in memory to go stale
            Assert.Equal(9, Settings().Value.MinimumNoticeDays);

            var model = await SaveAsync(Valid());

            // In the order of the week and of the day, written one way
            Assert.Equal(["Sunday", "Friday", "Saturday"], model.Days);
            Assert.Equal([new("08:00", "09:00"), new("16:00", "18:30")], model.StartRanges);
            Assert.Equal(("20:30", 15, 2, 30, "BlockTime", 2, 6, "America/Chicago"),
                (model.LatestEnd, model.SlotIncrementMinutes, model.MaxSessionsPerDay, model.BufferMinutes, model.OtherEvents, model.MinimumNoticeDays, model.HorizonMonths, model.TimeZone));

            var settings = Settings().Value;
            Assert.Equal("studio@example.com", settings.ContactEmailAddress);
            Assert.Equal(2, settings.MinimumNoticeDays);
            Assert.Equal(2, settings.StartWindows.Count);
            // Reading it back gives what saving answered with
            var read = await new GetBookingAvailabilitySettingsQueryHandler(Settings()).Handle(new(), CancellationToken.None);
            Assert.Equal(model.Days, read.Days);
            Assert.Equal(model.StartRanges, read.StartRanges);
            Assert.Equal(model with { Days = read.Days, StartRanges = read.StartRanges }, read);

            var row = Assert.Single(await host.Context.TenantSettings.ToListAsync());
            // One value per setting, however it was spelled before
            Assert.DoesNotContain("minimumNoticeDays", row.Json);
            Assert.Contains("\"MinimumNoticeDays\":2", row.Json);
        }

        public static TheoryData<string, Func<SaveBookingAvailabilitySettingsCommand, SaveBookingAvailabilitySettingsCommand>> Refused => new()
        {
            { "Days", c => c with { Days = [] } },
            { "Days", c => c with { Days = null } },
            { "Days", c => c with { Days = ["Saturday", "Caturday"] } },
            { "StartRanges", c => c with { StartRanges = [] } },
            { "StartRanges", c => c with { StartRanges = [new("8:00", "9:00"), new("10:00", "11:00"), new("12:00", "13:00"), new("14:00", "15:00"), new("16:00", "17:00")] } },
            { "StartRanges", c => c with { StartRanges = [new("9:00", "8:00")] } },
            { "StartRanges", c => c with { StartRanges = [new("8:10", "9:00")] } },
            { "StartRanges", c => c with { StartRanges = [new("8:00", null)] } },
            { "StartRanges", c => c with { StartRanges = [new("8:00", "10:00"), new("10:00", "11:00")] } },
            { "LatestEnd", c => c with { LatestEnd = "08:00" } },
            { "LatestEnd", c => c with { LatestEnd = "late" } },
            { "SlotIncrementMinutes", c => c with { SlotIncrementMinutes = 20 } },
            { "MaxSessionsPerDay", c => c with { MaxSessionsPerDay = 5 } },
            { "MaxSessionsPerDay", c => c with { MaxSessionsPerDay = -1 } },
            { "BufferMinutes", c => c with { BufferMinutes = 20 } },
            { "BufferMinutes", c => c with { BufferMinutes = 255 } },
            { "OtherEvents", c => c with { OtherEvents = "Ignore" } },
            { "MinimumNoticeDays", c => c with { MinimumNoticeDays = 61 } },
            { "MinimumNoticeDays", c => c with { MinimumNoticeDays = 45, HorizonMonths = 1 } },
            { "HorizonMonths", c => c with { HorizonMonths = 0 } },
            { "HorizonMonths", c => c with { HorizonMonths = 13 } },
            { "TimeZone", c => c with { TimeZone = "Europe/London" } },
        };

        [Theory]
        [MemberData(nameof(Refused))]
        public async Task A_value_outside_what_the_page_offers_is_refused_and_nothing_is_saved(string field,
            Func<SaveBookingAvailabilitySettingsCommand, SaveBookingAvailabilitySettingsCommand> change)
        {
            var error = await Assert.ThrowsAsync<CustomValidationException>(() => SaveAsync(change(Valid())));

            Assert.Contains(field, error.Errors.Keys);
            Assert.Empty(await host.Context.TenantSettings.ToListAsync());
        }

        [Fact]
        public async Task What_is_saved_is_what_the_calendar_then_offers()
        {
            await SaveAsync(Valid() with { TimeZone = "America/New_York" });

            var rules = BookingRules.FromSettings(Settings().Value);
            var saturday = new DateOnly(2026, 12, 12);
            var slots = SlotCalculator.GetSlots(rules, TimeSpan.FromMinutes(60), saturday, saturday.AddDays(2), [], new DateTimeOffset(2026, 11, 27, 17, 0, 0, TimeSpan.Zero));

            // Saturday and Sunday, not Monday
            Assert.Equal([saturday, saturday.AddDays(1)], slots.Keys);
            // 8:00 to 9:00 and 4:00 to 6:30, every quarter of an hour
            Assert.Equal(5 + 11, slots[saturday].Count);
        }

        private Task<BookingAvailabilitySettingsModel> SaveAsync(SaveBookingAvailabilitySettingsCommand command) =>
            new SaveBookingAvailabilitySettingsCommandHandler(host.Context, cache, host.TenantAccessor, Settings()).Handle(command, CancellationToken.None);

        private TenantSettingsReader<BookingSettings> Settings() =>
            new(Options.Create(new BookingSettings()), host.Context, host.TenantAccessor, cache, Options.Create(new TenantSettingsOptions()));
    }
}
