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
    /// <param name="Id">IANA name, e.g. "America/Chicago"</param>
    public sealed record BookingTimeZoneOption(string Id, string Name);

    /// <param name="TimeZone">The zone the site's booking hours are in</param>
    /// <param name="Options">The zones an owner can choose from</param>
    public sealed record BookingTimeZoneModel(string TimeZone, IReadOnlyList<BookingTimeZoneOption> Options);

    /// <summary>
    /// The time zones a site's booking hours can be in. Booking hours ("7:30 to 10:30", "finished by 8 PM")
    /// are wall-clock times in the photographer's zone; clients always see them in their own.
    /// </summary>
    public static class BookingTimeZones
    {
        public static readonly IReadOnlyList<BookingTimeZoneOption> All =
        [
            new("America/New_York", "Eastern Time"),
            new("America/Chicago", "Central Time"),
            new("America/Denver", "Mountain Time"),
            new("America/Phoenix", "Arizona (no daylight time)"),
            new("America/Los_Angeles", "Pacific Time"),
            new("America/Anchorage", "Alaska Time"),
            new("Pacific/Honolulu", "Hawaii Time"),
        ];

        public static BookingTimeZoneOption? Find(string? id) =>
            All.FirstOrDefault(z => string.Equals(z.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The current site's booking time zone, and the ones it can be changed to
    /// </summary>
    [Authorize(Policy = PolicyNames.ProductRead)]
    public record GetBookingTimeZoneQuery : IRequest<BookingTimeZoneModel>;

    public class GetBookingTimeZoneQueryHandler(ITenantSettings<BookingSettings> settings) : IRequestHandler<GetBookingTimeZoneQuery, BookingTimeZoneModel>
    {
        public Task<BookingTimeZoneModel> Handle(GetBookingTimeZoneQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(new BookingTimeZoneModel(settings.Value.TimeZone, BookingTimeZones.All));
    }

    /// <summary>
    /// Changes the zone the site's booking hours are in. Only that one value of the site's booking settings
    /// is touched; everything else it has set stays as it is. Bookings already made keep their times: a
    /// calendar event is an instant, not a wall-clock time.
    /// </summary>
    [Authorize(Policy = PolicyNames.ProductWrite)]
    public record SetBookingTimeZoneCommand(string? TimeZone) : IRequest<BookingTimeZoneModel>;

    public class SetBookingTimeZoneCommandHandler(ElysianContext context, IMemoryCache cache,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor) : IRequestHandler<SetBookingTimeZoneCommand, BookingTimeZoneModel>
    {
        public async Task<BookingTimeZoneModel> Handle(SetBookingTimeZoneCommand request, CancellationToken cancellationToken)
        {
            var zone = BookingTimeZones.Find(request.TimeZone);
            // The list is what the screen offers; the second check is that this machine knows the zone too
            if (zone == null || !TimeZoneInfo.TryFindSystemTimeZoneById(zone.Id, out _))
            {
                throw new CustomValidationException([new ValidationFailure(nameof(request.TimeZone), "Please choose a time zone from the list.")]);
            }

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
                throw new ConflictException("This site's booking settings can't be read, so the time zone wasn't changed.");
            }

            // Settings are read without regard to case, so "timeZone" and "TimeZone" are the same value
            foreach (var existing in json.Properties().Where(p => string.Equals(p.Name, nameof(BookingSettings.TimeZone), StringComparison.OrdinalIgnoreCase)).ToList())
            {
                existing.Remove();
            }
            json[nameof(BookingSettings.TimeZone)] = zone.Id;
            row.Json = json.ToString(Newtonsoft.Json.Formatting.None);

            await context.SaveChangesAsync(cancellationToken);

            // This instance reads the new value at once; others pick it up when their copy runs out
            var tenantId = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Id!;
            cache.Remove(TenantSettingsSection.CacheKey(tenantId, BookingSettings.SectionName));

            return new BookingTimeZoneModel(zone.Id, BookingTimeZones.All);
        }
    }
}
