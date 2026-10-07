using System.Text.Json;

namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Thin JSON client for Microsoft Graph v1.0 with app-only credentials. Maps errors to the Graph*Exception types
    /// and retries throttling (429) and unavailability (503), honoring Retry-After.
    /// </summary>
    public interface IGraphApiClient
    {
        /// <summary>
        /// Sends a request and deserializes the response body, or returns default for an empty body (204)
        /// </summary>
        /// <param name="path">Relative to https://graph.microsoft.com/v1.0/, e.g. "users/{id}"</param>
        /// <param name="headers">Extra request headers, e.g. ConsistencyLevel for advanced queries</param>
        Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null,
            IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);

        Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Follows @odata.nextLink and returns every item of a collection
        /// </summary>
        Task<List<T>> GetAllPagesAsync<T>(string path, IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs GET requests through $batch, 20 per call. Returns each path's body, or null when Graph answered 404.
        /// Other per-request errors throw.
        /// </summary>
        Task<Dictionary<string, JsonElement?>> BatchGetAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    }
}
