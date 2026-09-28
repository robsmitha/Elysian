namespace Elysian.Application.Interfaces
{
    public interface IHumanVerificationService
    {
        /// <summary>
        /// Verifies a challenge token issued to the browser (e.g. Cloudflare Turnstile). Tokens are single use,
        /// so a false result means the browser needs a fresh token. Fails closed when the provider can't be reached.
        /// </summary>
        Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken = default);
    }
}
