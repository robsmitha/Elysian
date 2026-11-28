using System.Net;
using System.Text.Json.Serialization;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Contact;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Contact.Commands
{
    /// <summary>
    /// Public contact form submission, emailed to Resend:ToEmailAddress with the visitor as Reply-To.
    /// Anonymous by design: protected by a honeypot (<see cref="Website"/>) and human verification
    /// (<see cref="TurnstileToken"/>). Hosts should also rate limit the endpoint per client.
    /// A message that reads like a sales pitch is still delivered, with its subject marked
    /// (<see cref="ContactSpamRules"/>, <see cref="IContactSpamFilter"/>).
    /// </summary>
    /// <param name="ReferralSource">How the visitor heard about the business, e.g. "Instagram" or "Other: a wedding fair". Optional.</param>
    public record SendContactMessageCommand(
        string? Name,
        string? Email,
        string? Phone,
        string? Subject,
        string? Message,
        string? TurnstileToken,
        string? Website,
        string? ReferralSource = null) : IRequest<bool>
    {
        /// <summary>
        /// Caller's IP, passed to the verification provider. Set by the host, never read from the request body.
        /// </summary>
        [JsonIgnore]
        public string? RemoteIp { get; init; }

        /// <summary>
        /// Honeypot: hidden from people, so anything filled in here came from a bot
        /// </summary>
        public bool IsBot => !string.IsNullOrWhiteSpace(Website);

        // Keeps visitor details and the single-use token out of request logging
        public override string ToString() => $"{nameof(SendContactMessageCommand)} {{ Subject = {Subject}, IsBot = {IsBot} }}";
    }

    public class SendContactMessageCommandValidator : AbstractValidator<SendContactMessageCommand>
    {
        public SendContactMessageCommandValidator()
        {
            // Bots get a quiet success from the handler rather than a list of what to fix
            When(v => !v.IsBot, () =>
            {
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

                RuleFor(v => v.Subject)
                    .NotEmpty().WithMessage("Please enter a subject.")
                    .MaximumLength(200).WithMessage("Subject must be 200 characters or fewer.");

                RuleFor(v => v.Message)
                    .MaximumLength(5000).WithMessage("Message must be 5000 characters or fewer.");

                RuleFor(v => v.ReferralSource)
                    .MaximumLength(200).WithMessage("How you heard about us must be 200 characters or fewer.");

                RuleFor(v => v.TurnstileToken)
                    .NotEmpty().WithMessage("Please complete the verification.");
            });
        }
    }

    public class SendContactMessageCommandHandler(IHumanVerificationService humanVerificationService, IEmailService emailService,
        ITenantSettings<ResendSettings> resendSettings, ILogger<SendContactMessageCommandHandler> logger,
        Elysian.Infrastructure.Context.ElysianContext? context = null, IContactSpamFilter? spamFilter = null)
        : IRequestHandler<SendContactMessageCommand, bool>
    {
        /// <summary>Starts the subject of a message that reads like a sales pitch, so an inbox rule can file it</summary>
        public const string SpamSubjectPrefix = "[Possible spam] ";

        public async Task<bool> Handle(SendContactMessageCommand request, CancellationToken cancellationToken)
        {
            if (request.IsBot)
            {
                logger.LogInformation("Contact submission dropped by honeypot");
                return true;
            }

            if (!await humanVerificationService.VerifyAsync(request.TurnstileToken!, request.RemoteIp, cancellationToken))
            {
                throw new CustomValidationException([
                    new ValidationFailure(nameof(request.TurnstileToken),
                        "We couldn't verify you're human. Please complete the verification again and resubmit.")
                ]);
            }

            // The inbox the site was set up with, or else the address its owner confirmed in the admin app.
            // Never an address that hasn't been confirmed, and never another site's.
            var toAddress = resendSettings.Value.ToEmailAddress;
            if (string.IsNullOrWhiteSpace(toAddress) && context != null)
            {
                toAddress = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    System.Linq.Queryable.Select(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(context.TenantContactEmails), c => c.Email),
                    cancellationToken) ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(toAddress))
            {
                throw new InvalidOperationException("This site has no contact email yet, so the message wasn't sent. Its owner sets one in the admin app.");
            }

            var name = request.Name!.Trim();
            var email = request.Email!.Trim();
            var subject = request.Subject!.Trim();
            var phone = string.IsNullOrWhiteSpace(request.Phone) ? "Not provided" : request.Phone.Trim();
            var message = string.IsNullOrWhiteSpace(request.Message) ? "No message provided." : request.Message.Trim();
            var referral = string.IsNullOrWhiteSpace(request.ReferralSource) ? "Not provided" : request.ReferralSource.Trim();

            // Read for a sales pitch. Whatever the answer, the message is delivered: a flagged one is marked,
            // never dropped, so a wrong guess can't lose an inquiry.
            var verdict = spamFilter == null
                ? ContactSpamVerdict.NotChecked
                : await spamFilter.CheckAsync(new ContactMessageText(name, email, subject, message), cancellationToken);
            string? flag = null;
            if (verdict.IsSpam)
            {
                flag = $"Marked as possible spam{(string.IsNullOrWhiteSpace(verdict.Reason) ? "" : $": {verdict.Reason}")}. Delivered anyway in case it is not.";
                logger.LogInformation("Contact submission marked as possible spam by {Source}: {Reason}", verdict.Source, verdict.Reason);
            }

            await emailService.SendAsync(new OutgoingEmail(
                To: toAddress,
                Subject: $"{(verdict.IsSpam ? SpamSubjectPrefix : "")}New inquiry from {name}: {subject}",
                HtmlBody: BuildHtml(name, email, phone, subject, referral, message, flag),
                TextBody: $"{(flag == null ? "" : $"{flag}\n\n")}Name: {name}\nEmail: {email}\nPhone: {phone}\nSubject: {subject}\nHeard about us: {referral}\n\n{message}",
                ReplyTo: email), cancellationToken);

            return true;
        }

        private static string BuildHtml(string name, string email, string phone, string subject, string referral, string message, string? flag = null)
        {
            static string Encode(string value) => WebUtility.HtmlEncode(value);

            var note = flag == null ? "" : $"<p style=\"font-family: Arial, sans-serif; font-size: 13px; color: #8a5a00; background: #fff6e0; padding: 8px 12px;\">{Encode(flag)}</p>";

            return $"""
                {note}
                <h2 style="font-family: Georgia, serif; font-weight: normal;">New contact form inquiry</h2>
                <table cellpadding="6" style="font-family: Arial, sans-serif; font-size: 14px;">
                    <tr><td><strong>Name</strong></td><td>{Encode(name)}</td></tr>
                    <tr><td><strong>Email</strong></td><td>{Encode(email)}</td></tr>
                    <tr><td><strong>Phone</strong></td><td>{Encode(phone)}</td></tr>
                    <tr><td><strong>Subject</strong></td><td>{Encode(subject)}</td></tr>
                    <tr><td><strong>Heard about us</strong></td><td>{Encode(referral)}</td></tr>
                </table>
                <p style="font-family: Arial, sans-serif; font-size: 14px; white-space: pre-wrap;">{Encode(message)}</p>
                """;
        }
    }
}
