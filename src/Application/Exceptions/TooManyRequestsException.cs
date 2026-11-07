namespace Elysian.Application.Exceptions
{
    /// <summary>A rate limit was hit; the request can be tried again at <see cref="RetryAt"/>. Becomes HTTP 429.</summary>
    public class TooManyRequestsException(string message, DateTimeOffset retryAt) : Exception(message)
    {
        public DateTimeOffset RetryAt { get; } = retryAt;
    }
}
