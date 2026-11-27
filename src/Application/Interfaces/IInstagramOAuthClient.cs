namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Meta's Business Login for Instagram: the photographer approves on Instagram and the platform's own Meta app
    /// receives a token for their account. Failures throw <see cref="Exceptions.InstagramApiException"/>.
    /// </summary>
    public interface IInstagramOAuthClient
    {
        /// <summary>
        /// False until the platform has its Meta app's id, secret and return address configured
        /// </summary>
        bool CanAuthorize { get; }

        /// <summary>
        /// Where to send the photographer so they can approve on Instagram
        /// </summary>
        string AuthorizationUrl(string state);

        /// <summary>
        /// Exchanges the code Instagram sent back for a long-lived (60 day) token. A code Instagram no longer accepts
        /// throws with <see cref="Exceptions.InstagramApiException.StatusCode"/> 400.
        /// </summary>
        Task<InstagramAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default);
    }

    /// <param name="AccessToken">Secret: never log it or return it from an endpoint</param>
    /// <param name="Permissions">What the photographer allowed; empty when Instagram didn't say</param>
    public record InstagramAuthorization(string AccessToken, TimeSpan? ExpiresIn, IReadOnlyList<string> Permissions)
    {
        public override string ToString() =>
            $"{nameof(InstagramAuthorization)} {{ ExpiresIn = {ExpiresIn}, Permissions = {string.Join(',', Permissions)} }}";
    }
}
