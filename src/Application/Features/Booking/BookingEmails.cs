using System.Globalization;
using System.Net;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;

namespace Elysian.Application.Features.Booking
{
    /// <summary>
    /// Emails sent when a session is booked. Every client-supplied value is HTML-encoded.
    /// </summary>
    public static class BookingEmails
    {
        private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

        public record Details(string Title, int DurationMinutes, decimal? Price, bool IsPriceFrom, string? Location,
            DateTimeOffset Start, string Name, string Email, string? Phone, string? Message);

        /// <summary>
        /// Inbox told about bookings and calendar problems: Booking:NotificationEmailAddress, else Resend:ToEmailAddress
        /// </summary>
        public static string? OwnerAddress(BookingSettings booking, ResendSettings resend) =>
            string.IsNullOrWhiteSpace(booking.NotificationEmailAddress) ? NullIfEmpty(resend.ToEmailAddress) : booking.NotificationEmailAddress;

        public static string? FromAddress(BookingSettings booking) => NullIfEmpty(booking.FromEmailAddress);

        public static OutgoingEmail ToClient(Details details, TimeZoneInfo clientTimeZone, BookingSettings booking)
        {
            var when = FormatWhen(details.Start, clientTimeZone);
            var contact = NullIfEmpty(booking.ContactEmailAddress);
            var contactLine = contact == null ? "just reply to this email" : $"email me at {contact}";

            var text = $"""
                Hi {details.Name},

                Thank you for booking! Here's what you requested:

                Session: {details.Title} ({FormatDuration(details.DurationMinutes)})
                Date: {when}
                Price: {FormatPrice(details.Price, details.IsPriceFrom)}
                {(details.Location == null ? "" : $"Location: {details.Location}\n")}
                What happens next: your time is being held for you. I'll be in touch within a day or two to collect your deposit, which confirms your session. If anything changes in the meantime, {contactLine}.

                I can't wait to work with you!
                """;

            var html = $"""
                <h2 style="font-family: Georgia, serif; font-weight: normal;">Thank you for booking, {Encode(details.Name)}!</h2>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">Here's what you requested:</p>
                {DetailsTable(details, when)}
                <p style="font-family: Arial, sans-serif; font-size: 14px;"><strong>What happens next:</strong> your time is being held for you.
                I'll be in touch within a day or two to collect your deposit, which confirms your session.
                If anything changes in the meantime, {Encode(contactLine)}.</p>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">I can't wait to work with you!</p>
                """;

            return new OutgoingEmail(details.Email, $"Your {details.Title} session request: {FormatDate(details.Start, clientTimeZone)}",
                html, text, ReplyTo: contact, From: FromAddress(booking));
        }

        public static OutgoingEmail ToOwner(Details details, TimeZoneInfo ownerTimeZone, string? clientTimeZone, string to, BookingSettings booking)
        {
            var when = FormatWhen(details.Start, ownerTimeZone);
            var phone = string.IsNullOrWhiteSpace(details.Phone) ? "Not provided" : details.Phone;
            var message = string.IsNullOrWhiteSpace(details.Message) ? "No message provided." : details.Message;

            var text = $"""
                New booking request (tentative on your calendar until the deposit is paid)

                Session: {details.Title} ({FormatDuration(details.DurationMinutes)})
                Date: {when}
                Price: {FormatPrice(details.Price, details.IsPriceFrom)}
                Client: {details.Name}
                Email: {details.Email}
                Phone: {phone}
                Client's time zone: {clientTimeZone ?? "Unknown"}

                {message}
                """;

            var html = $"""
                <h2 style="font-family: Georgia, serif; font-weight: normal;">New booking request</h2>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">It's on your calendar as <strong>tentative</strong>. Once the deposit is paid,
                change the event to confirmed and remove "PENDING" from its title.</p>
                {DetailsTable(details, when)}
                <table cellpadding="6" style="font-family: Arial, sans-serif; font-size: 14px;">
                    <tr><td><strong>Client</strong></td><td>{Encode(details.Name)}</td></tr>
                    <tr><td><strong>Email</strong></td><td>{Encode(details.Email)}</td></tr>
                    <tr><td><strong>Phone</strong></td><td>{Encode(phone)}</td></tr>
                    <tr><td><strong>Client's time zone</strong></td><td>{Encode(clientTimeZone ?? "Unknown")}</td></tr>
                </table>
                <p style="font-family: Arial, sans-serif; font-size: 14px; white-space: pre-wrap;">{Encode(message)}</p>
                """;

            return new OutgoingEmail(to, $"New booking: {details.Title} with {details.Name}, {FormatDate(details.Start, ownerTimeZone)}",
                html, text, ReplyTo: details.Email, From: FromAddress(booking));
        }

        /// <summary>
        /// Alert for the photographer when Google Calendar stops working
        /// </summary>
        public static OutgoingEmail CalendarNeedsAttention(string to, string problem, BookingSettings booking)
        {
            const string subject = "Action needed: online booking is paused (Google Calendar)";
            var text = $"""
                Online booking can't reach your Google Calendar, so clients are being sent to the Contact page instead.

                {problem}

                To fix it, sign in to the site, open Admin > Calendar, and paste a new refresh token.
                """;
            var html = $"""
                <h2 style="font-family: Georgia, serif; font-weight: normal;">Online booking is paused</h2>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">Online booking can't reach your Google Calendar, so clients are being sent to the Contact page instead.</p>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">{Encode(problem)}</p>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">To fix it, sign in to the site, open <strong>Admin &rsaquo; Calendar</strong>, and paste a new refresh token.</p>
                """;
            return new OutgoingEmail(to, subject, html, text, From: FromAddress(booking));
        }

        /// <summary>
        /// "Sunday, October 18, 2026 at 7:30 AM Eastern Daylight Time"
        /// </summary>
        public static string FormatWhen(DateTimeOffset start, TimeZoneInfo timeZone)
        {
            var local = TimeZoneInfo.ConvertTime(start, timeZone);
            var zoneName = timeZone.IsDaylightSavingTime(local) ? timeZone.DaylightName : timeZone.StandardName;
            return $"{local.ToString("dddd, MMMM d, yyyy 'at' h:mm tt", Culture)} {zoneName}";
        }

        public static string FormatPrice(decimal? price, bool isPriceFrom) =>
            price is decimal p ? $"{p.ToString(p % 1 == 0 ? "C0" : "C2", Culture)}{(isPriceFrom ? "+" : "")}" : "Inquire for pricing";

        public static string FormatDuration(int minutes) => minutes switch
        {
            < 60 => $"{minutes} minutes",
            60 => "1 hour",
            _ when minutes % 60 == 0 => $"{minutes / 60} hours",
            _ => $"{minutes / 60.0:0.#} hours",
        };

        private static string FormatDate(DateTimeOffset start, TimeZoneInfo timeZone) =>
            TimeZoneInfo.ConvertTime(start, timeZone).ToString("MMMM d, yyyy", Culture);

        private static string DetailsTable(Details details, string when) => $"""
            <table cellpadding="6" style="font-family: Arial, sans-serif; font-size: 14px;">
                <tr><td><strong>Session</strong></td><td>{Encode(details.Title)} ({FormatDuration(details.DurationMinutes)})</td></tr>
                <tr><td><strong>Date</strong></td><td>{Encode(when)}</td></tr>
                <tr><td><strong>Price</strong></td><td>{Encode(FormatPrice(details.Price, details.IsPriceFrom))}</td></tr>
                {(details.Location == null ? "" : $"<tr><td><strong>Location</strong></td><td>{Encode(details.Location)}</td></tr>")}
            </table>
            """;

        private static string Encode(string value) => WebUtility.HtmlEncode(value);

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
