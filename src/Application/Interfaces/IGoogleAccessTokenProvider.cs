namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Hands out a working Google access token for the current tenant, refreshing (and saving any rotated refresh token) as needed
    /// </summary>
    public interface IGoogleAccessTokenProvider
    {
        /// <param name="forceRefresh">Ignore the cached access token, e.g. after Google rejected it or to prove the refresh token still works</param>
        Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    }
}
