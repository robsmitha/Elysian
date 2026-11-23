using Elysian.Application.Exceptions;
using Elysian.Application.Features.Booking;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.Booking
{
    public class BookingTimeZoneTests
    {
        private readonly TestHost host = new();
        private readonly MemoryCache cache = new(new MemoryCacheOptions());

        [Fact]
        public async Task A_site_starts_in_eastern_time_and_is_offered_the_us_zones()
        {
            var model = await new GetBookingTimeZoneQueryHandler(Settings()).Handle(new GetBookingTimeZoneQuery(), CancellationToken.None);

            Assert.Equal("America/New_York", model.TimeZone);
            Assert.Contains(model.Options, z => z.Id == "America/Los_Angeles");
            Assert.All(model.Options, z => Assert.True(TimeZoneInfo.TryFindSystemTimeZoneById(z.Id, out _), z.Id));
        }

        [Fact]
        public async Task Choosing_a_zone_changes_what_the_site_reads_straight_away()
        {
            // Read once, so there is a copy in memory to go stale
            Assert.Equal("America/New_York", Settings().Value.TimeZone);

            var model = await SetAsync("america/chicago");

            Assert.Equal("America/Chicago", model.TimeZone);
            Assert.Equal("America/Chicago", Settings().Value.TimeZone);
        }

        [Fact]
        public async Task Only_the_zone_changes_in_settings_the_site_already_has()
        {
            host.Context.TenantSettings.Add(new TenantSetting { Section = "Booking", Json = """{"timeZone":"America/New_York","MinimumNoticeDays":3,"NotificationEmailAddress":"studio@example.com"}""" });
            await host.Context.SaveChangesAsync();

            await SetAsync("America/Denver");

            var settings = Settings().Value;
            Assert.Equal("America/Denver", settings.TimeZone);
            Assert.Equal(3, settings.MinimumNoticeDays);
            Assert.Equal("studio@example.com", settings.NotificationEmailAddress);
            Assert.Single(await host.Context.TenantSettings.ToListAsync());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Europe/London")]
        [InlineData("Not/AZone")]
        public async Task A_zone_that_is_not_on_the_list_is_refused(string? zone)
        {
            await Assert.ThrowsAsync<CustomValidationException>(() => SetAsync(zone));
            Assert.Empty(await host.Context.TenantSettings.ToListAsync());
        }

        private Task<BookingTimeZoneModel> SetAsync(string? zone) =>
            new SetBookingTimeZoneCommandHandler(host.Context, cache, host.TenantAccessor).Handle(new SetBookingTimeZoneCommand(zone), CancellationToken.None);

        private TenantSettingsReader<BookingSettings> Settings() =>
            new(Options.Create(new BookingSettings()), host.Context, host.TenantAccessor, cache, Options.Create(new TenantSettingsOptions()));
    }
}
