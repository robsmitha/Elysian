using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using Resend;

namespace Elysian.Infrastructure.Services
{
    public class ResendEmailService(IResend resend, IOptions<ResendSettings> options) : IEmailService
    {
        public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
        {
            var message = new EmailMessage
            {
                From = string.IsNullOrWhiteSpace(email.From) ? options.Value.FromEmailAddress : email.From,
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
    }
}
