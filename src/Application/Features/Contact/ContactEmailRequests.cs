using System.Net;
using System.Security.Cryptography;
using System.Text;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Contact
{
    /// <param name="Email">The confirmed address contact form messages go to, or null</param>
    /// <param name="PendingEmail">An address waiting for its code</param>
    public sealed record ContactEmailModel(string? Email, DateTimeOffset? VerifiedAt, string? PendingEmail, DateTimeOffset? CodeExpiresAt);

    /// <summary>
    /// Where the current site's contact form messages go
    /// </summary>
    [Authorize(Policy = PolicyNames.SiteRead)]
    public record GetContactEmailQuery : IRequest<ContactEmailModel>;

    public class GetContactEmailQueryHandler(ElysianContext context, TimeProvider timeProvider) : IRequestHandler<GetContactEmailQuery, ContactEmailModel>
    {
        public async Task<ContactEmailModel> Handle(GetContactEmailQuery request, CancellationToken cancellationToken) =>
            ContactEmails.Model(await context.TenantContactEmails.AsNoTracking().FirstOrDefaultAsync(cancellationToken), timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Sends a six-digit code to an address, to prove whoever asked can read it. Nothing about the site changes
    /// until the code is confirmed: messages keep going to the address already confirmed, if there is one.
    /// An owner's call, because it decides where the site's inquiries go.
    /// </summary>
    [Authorize(Policy = PolicyNames.SitePublish)]
    public record RequestContactEmailCommand(string? Email) : IRequest<ContactEmailModel>
    {
        // Keeps the address out of request logging
        public override string ToString() => nameof(RequestContactEmailCommand);
    }

    public class RequestContactEmailCommandHandler(ElysianContext context, IEmailService emailService, TimeProvider timeProvider,
        IOptions<PlatformEmailSettings> options, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<RequestContactEmailCommand, ContactEmailModel>
    {
        /// <summary>
        /// One code a minute: enough for a mistyped address, not enough to pester someone else's inbox
        /// </summary>
        public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(60);

        public async Task<ContactEmailModel> Handle(RequestContactEmailCommand request, CancellationToken cancellationToken)
        {
            var email = (request.Email ?? string.Empty).Trim();
            // Exactly one plain address: no display name, no list, nothing that could start another header
            if (email.Length is 0 or > 320 || email.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is ',' or ';' or '<' or '>' or '"')
                || !System.Net.Mail.MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            {
                throw new CustomValidationException([new ValidationFailure(nameof(request.Email), "Please enter a valid email address.")]);
            }

            var now = timeProvider.GetUtcNow();
            var row = await context.TenantContactEmails.FirstOrDefaultAsync(cancellationToken);
            if (row?.CodeSentAt is { } sentAt && now - sentAt < MinimumGap)
            {
                throw new ConflictException("A code was sent a moment ago. Please wait a minute before asking for another.");
            }

            if (row == null)
            {
                row = new TenantContactEmail();
                context.TenantContactEmails.Add(row);
            }

            var tenant = multiTenantContextAccessor.MultiTenantContext.TenantInfo!;
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            row.PendingEmail = email;
            row.CodeHash = ContactEmails.Hash(tenant.Id!, code);
            row.CodeSentAt = now;
            row.CodeExpiresAt = now.AddMinutes(Math.Max(1, options.Value.CodeLifetimeMinutes));
            row.FailedAttempts = 0;

            // Saved before sending, so asking again straight away is refused even if the send is slow
            await context.SaveChangesAsync(cancellationToken);

            var siteName = string.IsNullOrWhiteSpace(tenant.Name) ? tenant.Identifier! : tenant.Name;
            await emailService.SendAsync(ContactEmails.CodeEmail(email, siteName, code, options.Value.CodeLifetimeMinutes), cancellationToken);

            return ContactEmails.Model(row, now);
        }
    }

    /// <summary>
    /// Confirms the code sent by <see cref="RequestContactEmailCommand"/>. From then on the site's contact form
    /// messages go to that address.
    /// </summary>
    [Authorize(Policy = PolicyNames.SitePublish)]
    public record ConfirmContactEmailCommand(string? Code) : IRequest<ContactEmailModel>
    {
        public override string ToString() => nameof(ConfirmContactEmailCommand);
    }

    public class ConfirmContactEmailCommandHandler(ElysianContext context, TimeProvider timeProvider,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor) : IRequestHandler<ConfirmContactEmailCommand, ContactEmailModel>
    {
        public const int MaxAttempts = 5;

        public async Task<ContactEmailModel> Handle(ConfirmContactEmailCommand request, CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();
            var row = await context.TenantContactEmails.FirstOrDefaultAsync(cancellationToken);

            if (row?.PendingEmail == null || row.CodeHash == null || row.CodeExpiresAt == null || row.CodeExpiresAt <= now)
            {
                throw Invalid("That code has expired. Please ask for a new one.");
            }
            if (row.FailedAttempts >= MaxAttempts)
            {
                throw Invalid("Too many wrong codes. Please ask for a new one.");
            }

            var code = new string((request.Code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
            var expected = Convert.FromHexString(row.CodeHash);
            var given = Convert.FromHexString(ContactEmails.Hash(multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Id!, code));
            if (!CryptographicOperations.FixedTimeEquals(expected, given))
            {
                row.FailedAttempts++;
                await context.SaveChangesAsync(cancellationToken);
                throw Invalid("That code isn't right. Please check it and try again.");
            }

            row.Email = row.PendingEmail;
            row.VerifiedAt = now;
            row.PendingEmail = null;
            row.CodeHash = null;
            row.CodeExpiresAt = null;
            row.FailedAttempts = 0;
            await context.SaveChangesAsync(cancellationToken);

            return ContactEmails.Model(row, now);
        }

        private static CustomValidationException Invalid(string message) =>
            new([new ValidationFailure(nameof(ConfirmContactEmailCommand.Code), message)]);
    }

    public static class ContactEmails
    {
        public static ContactEmailModel Model(TenantContactEmail? row, DateTimeOffset now)
        {
            // A code that has run out is not something still waiting to be confirmed
            var pending = row?.PendingEmail != null && row.CodeExpiresAt > now;
            return new ContactEmailModel(row?.Email, row?.VerifiedAt, pending ? row!.PendingEmail : null, pending ? row!.CodeExpiresAt : null);
        }

        /// <summary>
        /// Tied to the tenant, so a code for one site is worth nothing on another
        /// </summary>
        public static string Hash(string tenantId, string code) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"contact-email\n{tenantId}\n{code}")));

        public static OutgoingEmail CodeEmail(string to, string siteName, string code, int lifetimeMinutes)
        {
            var subject = $"{code} is your code for {siteName}";
            var text = $"""
                Someone asked for messages from the {siteName} website's contact form to be sent to this address.

                To confirm, enter this code in the admin app: {code}

                It works for {lifetimeMinutes} minutes. If this wasn't you, ignore this email and nothing will change.
                """;
            var html = $"""
                <p style="font-family: Arial, sans-serif; font-size: 14px;">Someone asked for messages from the {WebUtility.HtmlEncode(siteName)} website's contact form to be sent to this address.</p>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">To confirm, enter this code in the admin app:</p>
                <p style="font-family: Arial, sans-serif; font-size: 28px; letter-spacing: 4px;"><strong>{code}</strong></p>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">It works for {lifetimeMinutes} minutes. If this wasn't you, ignore this email and nothing will change.</p>
                """;
            return new OutgoingEmail(to, subject, html, text);
        }
    }
}
