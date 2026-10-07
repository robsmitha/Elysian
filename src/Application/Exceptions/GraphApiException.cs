namespace Elysian.Application.Exceptions
{
    /// <summary>
    /// Microsoft Graph returned an error or couldn't be reached. Messages carry Graph's error code and message for
    /// server logs; hosts must not pass them to clients. Never includes tokens or invitation redeem URLs.
    /// </summary>
    public class GraphApiException(string message, int? statusCode = null, string? errorCode = null, Exception? innerException = null)
        : Exception(message, innerException)
    {
        /// <summary>
        /// HTTP status code, or null when no response was received
        /// </summary>
        public int? StatusCode { get; } = statusCode;

        /// <summary>
        /// Graph's error code from the response body, e.g. "Request_ResourceNotFound"
        /// </summary>
        public string? ErrorCode { get; } = errorCode;
    }

    /// <summary>
    /// The user, assignment or other object doesn't exist (404)
    /// </summary>
    public class GraphNotFoundException(string message, int? statusCode = null, string? errorCode = null)
        : GraphApiException(message, statusCode, errorCode);

    /// <summary>
    /// The object already exists, e.g. an app role assignment that's already in place
    /// </summary>
    public class GraphConflictException(string message, int? statusCode = null, string? errorCode = null)
        : GraphApiException(message, statusCode, errorCode);

    /// <summary>
    /// The app registration lacks a permission (403). A configuration problem, not the caller's fault.
    /// </summary>
    public class GraphForbiddenException(string message, int? statusCode = null, string? errorCode = null)
        : GraphApiException(message, statusCode, errorCode);

    /// <summary>
    /// Graph is still throttling (429) or unavailable (503) after retries
    /// </summary>
    public class GraphThrottledException(string message, int? statusCode = null, string? errorCode = null, TimeSpan? retryAfter = null)
        : GraphApiException(message, statusCode, errorCode)
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }
}
