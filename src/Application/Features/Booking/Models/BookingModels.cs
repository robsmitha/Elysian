using Elysian.Application.Features.Photos.Models;

namespace Elysian.Application.Features.Booking.Models
{
    /// <summary>
    /// Public shape of a session product, used by the Investment and booking pages
    /// </summary>
    /// <param name="Slug">URL segment, stored as the product's serial number</param>
    /// <param name="IsPriceFrom">Variable pricing: the price is a starting point, shown as "$400+"</param>
    /// <param name="CoverPhoto">Chosen cover, else null (the site falls back to the portfolio category's cover)</param>
    public record BookingSessionModel(
        int ProductId,
        string Slug,
        string Title,
        string? Description,
        int DurationMinutes,
        decimal? Price,
        bool IsPriceFrom,
        string? Location,
        string Collection,
        List<string> Features,
        string? PortfolioCategory,
        bool IsBookable,
        int SortOrder,
        PortfolioPhotoModel? CoverPhoto);

    /// <summary>
    /// Bookable start times for one month. <see cref="Days"/> are the photographer's dates (with a day either side,
    /// so clients in other time zones see every time on their own dates); each slot is an exact instant with offset.
    /// </summary>
    /// <param name="TimeZone">Photographer's IANA time zone</param>
    public record BookingAvailabilityModel(
        string Slug,
        string TimeZone,
        int DurationMinutes,
        DateOnly FirstBookableDate,
        DateOnly LastBookableDate,
        List<BookingDayModel> Days);

    public record BookingDayModel(DateOnly Date, List<DateTimeOffset> Slots);

    public enum BookingAvailabilityStatus
    {
        Available,

        /// <summary>
        /// The calendar can't be read (not connected, revoked or unreachable): send clients to the Contact page
        /// </summary>
        Unavailable,
    }

    public record BookingAvailabilityResult(BookingAvailabilityStatus Status, BookingAvailabilityModel? Availability = null);

    public enum BookingStatus
    {
        Booked,

        /// <summary>
        /// The time isn't offered any more (taken since the client loaded it, or never valid)
        /// </summary>
        Conflict,

        /// <summary>
        /// The calendar can't be used right now: send clients to the Contact page
        /// </summary>
        Unavailable,
    }

    public record CreateBookingResult(BookingStatus Status, BookingConfirmationModel? Booking = null);

    public record BookingConfirmationModel(
        string Slug,
        string Title,
        DateTimeOffset Start,
        DateTimeOffset End,
        int DurationMinutes,
        decimal? Price,
        bool IsPriceFrom,
        string? Location,
        string Name,
        string Email);

    /// <summary>
    /// What the admin overview shows of the sessions booked through the site
    /// </summary>
    /// <param name="Available">False when the calendar can't be read right now (not connected, or Google unreachable)</param>
    /// <param name="Bookings">Soonest first</param>
    public record UpcomingBookingsModel(bool Available, List<UpcomingBookingModel> Bookings);

    /// <param name="Title">The event's title, without the "pending" marker</param>
    /// <param name="Tentative">Booked online and not yet confirmed by the photographer</param>
    public record UpcomingBookingModel(string Id, string Title, DateTimeOffset Start, DateTimeOffset End, bool Tentative);
}
