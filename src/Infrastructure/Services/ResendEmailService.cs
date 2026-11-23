using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Sends email through Resend, for the current tenant when there is one.
    ///
    /// A site with its own code sends exactly as before: from the caller's sender, or its configured one.
    ///
    /// A site drawn with a shared template ("cloud") never borrows the host's sender, which belongs to another
    /// site. It sends from its own sender address if it has one, and otherwise as its name at the platform's
    /// sending domain. It also has a daily allowance, so one site can't use up what every site shares.
    /// </summary>
    public class ResendEmailService(IResend resend, ITenantSettings<ResendSettings> options, IOptions<ResendSettings> hostOptions,
        IOptions<PlatformEmailSettings> platform, IServiceProvider services, TimeProvider timeProvider, ILogger<ResendEmailService> logger,
        IMultiTenantContextAccessor<ElysianTenantInfo>? multiTenantContextAccessor = null) : IEmailService
    {
        public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
        {
            var tenant = multiTenantContextAccessor?.MultiTenantContext?.TenantInfo;
            var isCloud = tenant?.Runtime == RuntimeNames.Cloud;

            // Worked out before anything is counted, so a site that can't send doesn't use its allowance
            var from = isCloud ? CloudSender(tenant!) : string.IsNullOrWhiteSpace(email.From) ? options.Value.FromEmailAddress : email.From;

            if (isCloud)
            {
                await CountAsync(tenant!, cancellationToken);
            }

            var message = new EmailMessage
            {
                From = from,
                Subject = email.Subject.ReplaceLineEndings(" "),
                HtmlBody = email.HtmlBody,
                TextBody = email.TextBody,
            };
            message.To.Add(email.To);

            if (!string.IsNullOrWhiteSpace(email.ReplyTo))
            {
                message.ReplyTo = email.ReplyTo;
            }

            await resend.EmailSendAsync(message, cancellationToken);
        }

        private string CloudSender(ElysianTenantInfo tenant)
        {
            // A sender the site was given for itself, which is one that differs from the host's
            var own = options.Value.FromEmailAddress;
            if (!string.IsNullOrWhiteSpace(own) && own != hostOptions.Value.FromEmailAddress)
            {
                return own;
            }

            var domain = platform.Value.SendingDomain?.Trim();
            if (string.IsNullOrEmpty(domain) || !TenantIdentifiers.IsWellFormed(tenant.Identifier))
            {
                throw new InvalidOperationException(
                    $"{PlatformEmailSettings.SectionName}:{nameof(PlatformEmailSettings.SendingDomain)} is not configured, so {tenant.Identifier} has no address to send from.");
            }

            return $"{DisplayName(tenant.Name, tenant.Identifier!)} <{tenant.Identifier}@{domain}>";
        }

        /// <summary>
        /// A site's name as the sender's display name. Anything that could end the name early or start another
        /// header is dropped: an owner chooses this text.
        /// </summary>
        public static string DisplayName(string? name, string fallback)
        {
            var cleaned = new string((name ?? string.Empty)
                .Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '\\' or ',' or ';' or ':' or '@'))
                .ToArray()).Trim();
            if (cleaned.Length > 60)
            {
                cleaned = cleaned[..60].Trim();
            }
            return $"\"{(cleaned.Length > 0 ? cleaned : fallback)}\"";
        }

        /// <summary>
        /// Counts one email against the tenant's day, and refuses once the day's allowance is used. The count
        /// is read and written without a lock: two sends at once may be counted as one, which makes the limit
        /// a little soft and never blocks a send it shouldn't.
        /// </summary>
        private async Task CountAsync(ElysianTenantInfo tenant, CancellationToken cancellationToken)
        {
            var limit = platform.Value.DailyLimitPerTenant;
            if (limit <= 0)
            {
                return;
            }

            // Resolved here, not in the constructor: this service is also used where there is no tenant, and
            // the tenant's context can't be made without one
            var context = services.GetRequiredService<ElysianContext>();
            var now = timeProvider.GetUtcNow();
            var today = DateOnly.FromDateTime(now.UtcDateTime);

            var row = await context.TenantEmailCounts.FirstOrDefaultAsync(c => c.Day == today, cancellationToken);
            if (row != null && row.Count >= limit)
            {
                logger.LogWarning("{Tenant} has reached its limit of {Limit} emails for {Day}", tenant.Identifier, limit, today);
                throw new TooManyRequestsException("This site has sent as many emails as it can today. Please try again tomorrow.",
                    new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            }

            if (row == null)
            {
                row = new TenantEmailCount { Day = today };
                context.TenantEmailCounts.Add(row);
            }
            row.Count++;

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Another request made today's row at the same moment. The email still goes; one isn't counted.
                logger.LogInformation(ex, "Today's email count for {Tenant} was being written by another request", tenant.Identifier);
                context.Entry(row).State = EntityState.Detached;
            }
        }
    }
}
