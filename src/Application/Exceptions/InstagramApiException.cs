namespace Elysian.Application.Exceptions
{
    /// <summary>
    /// Instagram returned an error or couldn't be reached. Messages never include the access token.
    /// </summary>
    public class InstagramApiException : Exception
    {
        /// <summary>
        /// Graph API error code for an invalid, expired or revoked access token
        /// </summary>
        public const int InvalidTokenErrorCode = 190;

        public const string InvalidTokenHelp = "The Instagram access token is invalid or expired (error 190). Generate a new long-lived "
            + "token for the Instagram account in the Meta app dashboard and update Instagram:AccessToken.";

        public InstagramApiException(string message, int? statusCode = null, int? errorCode = null, Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// HTTP status code, or null when no response was received
        /// </summary>
        public int? StatusCode { get; }

        /// <summary>
        /// Graph API error code from the response body, if any
        /// </summary>
        public int? ErrorCode { get; }

        public bool IsInvalidToken => ErrorCode == InvalidTokenErrorCode;

        public bool IsRateLimited => StatusCode == 429 || ErrorCode is 4 or 17 or 32 or 613;
    }
}
