namespace Elysian.Application.Interfaces
{
    public interface IEmailService
    {
        /// <summary>
        /// Sends from <see cref="OutgoingEmail.From"/>, or the configured sender address when that's empty. Callers are responsible for HTML-encoding
        /// any untrusted values placed in <see cref="OutgoingEmail.HtmlBody"/>.
        /// </summary>
        Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
    }

    /// <param name="From">Overrides the configured sender; must be on a domain verified with the email provider</param>
    public record OutgoingEmail(string To, string Subject, string HtmlBody, string TextBody, string? ReplyTo = null, string? From = null);
}
