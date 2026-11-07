using System.Net;
using Newtonsoft.Json.Linq;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Retries Plaid calls that failed for a temporary reason: HTTP 429 or 5xx, or Plaid's RATE_LIMIT_EXCEEDED,
    /// API_ERROR and INSTITUTION_ERROR types. Waits 1s, 4s, then 16s, with a little jitter. Item errors (sign in again,
    /// bad token) are never retried: they need the person, not time.
    /// </summary>
    public class PlaidRetryHandler(Func<TimeSpan, CancellationToken, Task>? delay = null) : DelegatingHandler
    {
        internal static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(16)];

        private static readonly HashSet<string> TemporaryTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "RATE_LIMIT_EXCEEDED", "API_ERROR", "INSTITUTION_ERROR"
        };

        // A public token works once; a retry after a lost response would always fail, so it isn't retried
        private static readonly HashSet<string> NeverRetried = new(StringComparer.OrdinalIgnoreCase)
        {
            "item/public_token/exchange", "item/remove"
        };

        private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.IsAbsoluteUri == true ? request.RequestUri.AbsolutePath.TrimStart('/') : request.RequestUri?.OriginalString ?? "";
            var body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var mediaType = request.Content?.Headers.ContentType;

            for (var attempt = 0; ; attempt++)
            {
                using var attemptRequest = Copy(request, body, mediaType);
                var response = await base.SendAsync(attemptRequest, cancellationToken);
                if (attempt >= Backoff.Length || NeverRetried.Contains(path) || !await IsTemporaryAsync(response, cancellationToken))
                {
                    return response;
                }

                response.Dispose();
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
                await wait(Backoff[attempt] + jitter, cancellationToken);
            }
        }

        private static async Task<bool> IsTemporaryAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                return false;
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                return true;
            }

            // Plaid also reports some temporary problems as 400s with an error_type
            await response.Content.LoadIntoBufferAsync();
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                var errorType = JObject.Parse(text)["error_type"]?.Value<string>();
                return errorType != null && TemporaryTypes.Contains(errorType);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return false;
            }
        }

        private static HttpRequestMessage Copy(HttpRequestMessage request, byte[]? body, System.Net.Http.Headers.MediaTypeHeaderValue? mediaType)
        {
            var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
            foreach (var header in request.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            if (body != null)
            {
                copy.Content = new ByteArrayContent(body);
                if (mediaType != null)
                {
                    copy.Content.Headers.ContentType = mediaType;
                }
            }
            return copy;
        }
    }
}
