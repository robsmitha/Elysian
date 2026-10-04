using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Booking.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Booking.Commands
{
    /// <summary>
    /// Public booking of a session. The requested start must be one the availability rules offer right now
    /// (re-checked against the calendar here), so a time sent by the client is never trusted on its own.
    /// Creates a tentative calendar event, then emails the client and the photographer.
    /// Anonymous by design: protected by a honeypot (<see cref="Website"/>) and human verification (<see cref="TurnstileToken"/>).
    /// Hosts should also rate limit the endpoint per client.
    /// </summary>
    /// <param name="TimeZone">Client's IANA time zone, used only to show times in their emails</param>
    public record CreateBookingCommand(
        DateTimeOffset? Start,
        string? Name,
        string? Email,
        string? Phone,
        string? Message,
        bool PoliciesAccepted,
        string? TimeZone,
        string? TurnstileToken,
        string? Website) : IRequest<CreateBookingResult>
    {
        /// <summary>
        /// Session slug from the route. Set by the host.
        /// </summary>
        [JsonIgnore]
        public string Slug { get; init; } = string.Empty;

        /// <summary>
        /// Caller's IP, passed to the verification provider. Set by the host, never read from the request body.
        /// </summary>
        [JsonIgnore]
        public string? RemoteIp { get; init; }

        /// <summary>
        /// Honeypot: hidden from people, so anything filled in here came from a bot
        /// </summary>
        public bool IsBot => !string.IsNullOrWhiteSpace(Website);

        // Keeps client details and the single-use token out of request logging
        public override string ToString() => $"{nameof(CreateBookingCommand)} {{ Slug = {Slug}, Start = {Start:O}, IsBot = {IsBot} }}";
    }

    public class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
    {
        public CreateBookingCommandValidator()
        {
            // Bots get a quiet success from the handler rather than a list of what to fix
            When(v => !v.IsBot, () =>
            {
                RuleFor(v => v.Start)
                    .NotNull().WithMessage("Please choose a time.");

                RuleFor(v => v.Name)
                    .NotEmpty().WithMessage("Please enter your name.")
                    .MaximumLength(100).WithMessage("Name must be 100 characters or fewer.");

                RuleFor(v => v.Email)
                    .NotEmpty().WithMessage("Please enter your email.")
                    .EmailAddress().WithMessage("Please enter a valid email address.")
                    .MaximumLength(320).WithMessage("Email must be 320 characters or fewer.");

                RuleFor(v => v.Phone)
                    .MaximumLength(30).WithMessage("Phone must be 30 characters or fewer.")
                    .Matches(@"^[0-9+()\-.\s]{7,30}$").WithMessage("Please enter a valid phone number.")
                    .When(v => !string.IsNullOrWhiteSpace(v.Phone));

                RuleFor(v => v.Message)
                    .MaximumLength(5000).WithMessage("Message must be 5000 characters or fewer.");

                RuleFor(v => v.PoliciesAccepted)
                    .Equal(true).WithMessage("Please confirm you've read and agree to the Payment and Gallery Policies.");

                RuleFor(v => v.TimeZone)
                    .MaximumLength(64);

                RuleFor(v => v.TurnstileToken)
                    .NotEmpty().WithMessage("Please complete the verification.");
            });
        }
    }

    public class CreateBookingCommandHandler(ElysianContext context, BookingAvailabilityService availabilityService,
        IGoogleCalendarService calendarService, IHumanVerificationService humanVerificationService, IEmailService emailService,
        IOptions<BookingSettings> bookingOptions, IOptions<ResendSettings> resendOptions,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor, ILogger<CreateBookingCommandHandler> logger)
        : IRequestHandler<CreateBookingCommand, CreateBookingResult>
    {
        /// <summary>
        /// Marks events created here, so they can be told apart from the photographer's own
        /// </summary>
        public const string SourceProperty = "elysianBooking";

        public async Task<CreateBookingResult> Handle(CreateBookingCommand request, CancellationToken cancellationToken)
        {
            if (request.IsBot)
            {
                logger.LogInformation("Booking submission dropped by honeypot");
                return new CreateBookingResult(BookingStatus.Booked);
            }

            var row = await context.FindSessionAsync(request.Slug, cancellationToken);
            if (row == null || !row.Session.IsBookable)
            {
                throw new NotFoundException();
            }

            if (!await humanVerificationService.VerifyAsync(request.TurnstileToken!, request.RemoteIp, cancellationToken))
            {
                throw new CustomValidationException([
                    new ValidationFailure(nameof(request.TurnstileToken),
                        "We couldn't verify you're human. Please complete the verification again and resubmit.")
                ]);
            }

            var rules = availabilityService.Rules;
            var duration = row.Session.DurationMinutes;
            var start = request.Start!.Value;
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, rules.TimeZone).DateTime);

            var name = request.Name!.Trim();
            var email = request.Email!.Trim();
            var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            var message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim();
            var clientTimeZone = !string.IsNullOrWhiteSpace(request.TimeZone)
                && TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone.Trim(), out var zone) ? zone : null;

            try
            {
                // Re-check right before booking: the time must still be one the rules offer, with nothing on the calendar since
                var slots = await availabilityService.GetSlotsAsync(duration, date, date, cancellationToken);
                if (!slots.TryGetValue(date, out var times) || !times.Contains(start))
                {
                    logger.LogInformation("Booking conflict: {Slug} at {Start:O} is no longer offered", row.Product.SerialNumber, start);
                    return new CreateBookingResult(BookingStatus.Conflict);
                }

                var created = await calendarService.CreateEventAsync(new NewCalendarEvent(
                    EventId(row.Product.ProductId, start, email),
                    $"PENDING – {row.Product.Name} – {name}",
                    EventDescription(row, start, name, email, phone, message, clientTimeZone),
                    start,
                    start.AddMinutes(duration),
                    rules.TimeZone.Id,
                    Tentative: true,
                    new Dictionary<string, string>
                    {
                        [SourceProperty] = "true",
                        ["sessionSlug"] = row.Product.SerialNumber,
                        ["productId"] = row.Product.ProductId.ToString(),
                    }), cancellationToken);

                if (!created)
                {
                    logger.LogInformation("Booking for {Slug} at {Start:O} was already created; repeated submission ignored", row.Product.SerialNumber, start);
                    return Booked(row, start, name, email);
                }
            }
            catch (CalendarAuthorizationException ex)
            {
                logger.LogError("Booking unavailable: {Failure}. {Message}", ex.Failure, ex.Message);
                return new CreateBookingResult(BookingStatus.Unavailable);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Booking unavailable: Google Calendar couldn't be read or updated");
                return new CreateBookingResult(BookingStatus.Unavailable);
            }

            logger.LogInformation("Booked {Slug} at {Start:O} (tentative)", row.Product.SerialNumber, start);
            await SendEmailsAsync(row, start, name, email, phone, message, clientTimeZone, cancellationToken);

            return Booked(row, start, name, email);
        }

        private static CreateBookingResult Booked(BookingSessions.SessionRow row, DateTimeOffset start, string name, string email) =>
            new(BookingStatus.Booked, new BookingConfirmationModel(
                row.Product.SerialNumber,
                row.Product.Name,
                start,
                start.AddMinutes(row.Session.DurationMinutes),
                row.Session.DurationMinutes,
                row.Product.Price,
                row.Product.IsPriceFrom(),
                row.Session.Location,
                name,
                email));

        /// <summary>
        /// Same booking, same id: Google rejects the second insert, so a double submission can't create two events.
        /// Hex digits are valid in Google's base32hex event ids.
        /// </summary>
        private string EventId(int productId, DateTimeOffset start, string email)
        {
            var tenant = multiTenantContextAccessor.MultiTenantContext.TenantInfo?.Identifier;
            var key = $"{tenant}|{productId}|{start.UtcDateTime:O}|{email.ToLowerInvariant()}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        }

        private string EventDescription(BookingSessions.SessionRow row, DateTimeOffset start, string name, string email,
            string? phone, string? message, TimeZoneInfo? clientTimeZone)
        {
            var builder = new StringBuilder()
                .AppendLine($"Session: {row.Product.Name} ({BookingEmails.FormatDuration(row.Session.DurationMinutes)})")
                .AppendLine($"Price: {BookingEmails.FormatPrice(row.Product.Price, row.Product.IsPriceFrom())}")
                .AppendLine("Status: PENDING DEPOSIT. Once it's paid, mark this event as confirmed and remove \"PENDING\" from the title.")
                .AppendLine()
                .AppendLine($"Client: {name}")
                .AppendLine($"Email: {email}")
                .AppendLine($"Phone: {phone ?? "Not provided"}");

            if (clientTimeZone != null && clientTimeZone.Id != availabilityService.Rules.TimeZone.Id)
            {
                builder.AppendLine($"Client's local time: {BookingEmails.FormatWhen(start, clientTimeZone)}");
            }

            builder.AppendLine().AppendLine("Message:").AppendLine(message ?? "No message provided.")
                .AppendLine().Append("Booked online through the website.");
            return builder.ToString();
        }

        /// <summary>
        /// The booking already exists on the calendar, so email failures are logged rather than failing the request
        /// </summary>
        private async Task SendEmailsAsync(BookingSessions.SessionRow row, DateTimeOffset start, string name, string email,
            string? phone, string? message, TimeZoneInfo? clientTimeZone, CancellationToken cancellationToken)
        {
            var booking = bookingOptions.Value;
            var details = new BookingEmails.Details(row.Product.Name, row.Session.DurationMinutes, row.Product.Price, row.Product.IsPriceFrom(),
                row.Session.Location, start, name, email, phone, message);

            try
            {
                await emailService.SendAsync(BookingEmails.ToClient(details, clientTimeZone ?? availabilityService.Rules.TimeZone, booking), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Booking confirmation email to the client failed for {Slug} at {Start:O}", row.Product.SerialNumber, start);
            }

            var owner = BookingEmails.OwnerAddress(booking, resendOptions.Value);
            if (owner == null)
            {
                logger.LogWarning("No booking notification address is configured (Booking:NotificationEmailAddress or Resend:ToEmailAddress)");
                return;
            }

            try
            {
                await emailService.SendAsync(BookingEmails.ToOwner(details, availabilityService.Rules.TimeZone, clientTimeZone?.Id, owner, booking),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Booking notification email failed for {Slug} at {Start:O}", row.Product.SerialNumber, start);
            }
        }
    }
}
