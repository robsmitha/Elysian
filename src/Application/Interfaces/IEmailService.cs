namespace Elysian.Application.Interfaces
{
    public interface IEmailService
    {
        /// <summary>
        /// Sends from the configured sender address. Callers are responsible for HTML-encoding
        /// any untrusted values placed in <see cref="OutgoingEmail.HtmlBody"/>.
        /// </summary>
        Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
    }

    public record OutgoingEmail(string To, string Subject, string HtmlBody, string TextBody, string? ReplyTo = null);
}
