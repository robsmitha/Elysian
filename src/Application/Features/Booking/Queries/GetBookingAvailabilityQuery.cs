using Elysian.Application.Exceptions;
using Elysian.Application.Features.Booking.Models;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Booking.Queries
{
    /// <summary>
    /// Public, anonymous: bookable start times for a session over one month of the calendar.
    /// Calendar problems come back as <see cref="BookingAvailabilityStatus.Unavailable"/> rather than an error.
    /// </summary>
    public record GetBookingAvailabilityQuery(string Slug, int Year, int Month) : IRequest<BookingAvailabilityResult>;

    public class GetBookingAvailabilityQueryValidator : AbstractValidator<GetBookingAvailabilityQuery>
    {
        public GetBookingAvailabilityQueryValidator()
        {
            RuleFor(v => v.Year).InclusiveBetween(2000, 2200).WithMessage("Please choose a valid month.");
            RuleFor(v => v.Month).InclusiveBetween(1, 12).WithMessage("Please choose a valid month.");
        }
    }

    public class GetBookingAvailabilityQueryHandler(ElysianContext context, BookingAvailabilityService availabilityService,
        TimeProvider timeProvider, ILogger<GetBookingAvailabilityQueryHandler> logger)
        : IRequestHandler<GetBookingAvailabilityQuery, BookingAvailabilityResult>
    {
        public async Task<BookingAvailabilityResult> Handle(GetBookingAvailabilityQuery request, CancellationToken cancellationToken)
        {
            var row = await context.FindSessionAsync(request.Slug, cancellationToken);
            if (row == null || !row.Session.IsBookable)
            {
                throw new NotFoundException();
            }

            // A day either side covers clients whose own dates differ from the photographer's
            var monthStart = new DateOnly(request.Year, request.Month, 1);
            var from = monthStart.AddDays(-1);
            var to = monthStart.AddMonths(1);

            SortedDictionary<DateOnly, List<DateTimeOffset>> slots;
            try
            {
                slots = await availabilityService.GetSlotsAsync(row.Session.DurationMinutes, from, to, cancellationToken);
            }
            catch (CalendarAuthorizationException ex)
            {
                logger.LogError("Booking availability unavailable: {Failure}. {Message}", ex.Failure, ex.Message);
                return new BookingAvailabilityResult(BookingAvailabilityStatus.Unavailable);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Booking availability unavailable: Google Calendar couldn't be read");
                return new BookingAvailabilityResult(BookingAvailabilityStatus.Unavailable);
            }

            var rules = availabilityService.Rules;
            var now = timeProvider.GetUtcNow();
            return new BookingAvailabilityResult(BookingAvailabilityStatus.Available, new BookingAvailabilityModel(
                row.Product.SerialNumber,
                rules.TimeZone.Id,
                row.Session.DurationMinutes,
                rules.FirstBookableDate(now),
                rules.LastBookableDate(now),
                slots.Select(d => new BookingDayModel(d.Key, d.Value)).ToList()));
        }
    }
}
