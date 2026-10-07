using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Elysian.Infrastructure.Services
{
    public class GraphRetryOptions
    {
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// First wait when Graph sends no Retry-After; doubles on each retry
        /// </summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Longest single wait. A longer Retry-After gives up instead of holding the request open.
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Microsoft Graph over a typed HttpClient. The token comes from the injected <see cref="TokenCredential"/>,
    /// which caches it. Invitation responses carry redeem URLs, so the HttpClient is registered without the
    /// factory's loggers and nothing here logs bodies; exception messages leave out query strings, which can hold emails.
    /// </summary>
    public class GraphApiClient(HttpClient httpClient, TokenCredential credential, GraphRetryOptions retryOptions,
        ILogger<GraphApiClient> logger) : IGraphApiClient
    {
        public const string BaseAddress = "https://graph.microsoft.com/v1.0/";
        private const int BatchSize = 20;
        private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

        public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null,
            IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            using var response = await SendWithRetryAsync(method, path, body, headers, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
        {
            using var _ = await SendWithRetryAsync(method, path, body, null, cancellationToken);
        }

        public async Task<List<T>> GetAllPagesAsync<T>(string path, IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            var items = new List<T>();
            string? next = path;
            while (next != null)
            {
                var page = await SendAsync<CollectionPage<T>>(HttpMethod.Get, next, headers: headers, cancellationToken: cancellationToken);
                items.AddRange(page?.Value ?? []);
                next = page?.NextLink;
            }
            return items;
        }

        public async Task<Dictionary<string, JsonElement?>> BatchGetAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, JsonElement?>();
            foreach (var chunk in paths.Distinct().Chunk(BatchSize))
            {
                // Request ids are positions in the chunk; only those Graph throttled are sent again.
                var pending = chunk.Select((p, i) => (Id: i.ToString(), Path: p)).ToList();
                for (var attempt = 0; pending.Count > 0; attempt++)
                {
                    var batch = new
                    {
                        requests = pending.Select(p => new { id = p.Id, method = "GET", url = "/" + p.Path.TrimStart('/') })
                    };
                    var response = await SendAsync<BatchResponse>(HttpMethod.Post, "$batch", batch, cancellationToken: cancellationToken)
                        ?? throw new GraphApiException("Graph's $batch response was empty.");

                    var throttled = new List<(string Id, string Path)>();
                    TimeSpan? wait = null;
                    foreach (var item in response.Responses ?? [])
                    {
                        var request = pending.FirstOrDefault(p => p.Id == item.Id);
                        if (request.Path == null) continue;

                        if (item.Status is >= 200 and < 300)
                        {
                            results[request.Path] = item.Body;
                        }
                        else if (item.Status == 404)
                        {
                            results[request.Path] = null;
                        }
                        else if (item.Status is 429 or 503)
                        {
                            throttled.Add(request);
                            var retryAfter = ParseRetryAfter(item.Headers?.GetValueOrDefault("Retry-After"));
                            if (retryAfter > wait || wait == null) wait = retryAfter;
                        }
                        else
                        {
                            throw CreateException("GET", request.Path, item.Status, ReadError(item.Body));
                        }
                    }

                    if (throttled.Count > 0)
                    {
                        if (attempt >= retryOptions.MaxRetries)
                        {
                            throw new GraphThrottledException($"Graph $batch still throttled after {attempt} retries.", 429, retryAfter: wait);
                        }
                        await DelayAsync(wait, attempt, cancellationToken);
                    }
                    pending = throttled;
                }
            }
            return results;
        }

        private async Task<HttpResponseMessage> SendWithRetryAsync(HttpMethod method, string path, object? body,
            IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);

                using var request = new HttpRequestMessage(method, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                foreach (var header in headers ?? new Dictionary<string, string>())
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                if (body != null)
                {
                    request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
                }

                HttpResponseMessage response;
                try
                {
                    response = await httpClient.SendAsync(request, cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    throw new GraphApiException($"Graph {method} {StripQuery(path)} could not be reached ({ex.GetType().Name}).", innerException: ex);
                }

                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                var status = (int)response.StatusCode;
                var retryAfter = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is DateTimeOffset date ? date - DateTimeOffset.UtcNow : null);

                if (status is 429 or 503)
                {
                    var tooLong = retryAfter > retryOptions.MaxDelay;
                    if (attempt < retryOptions.MaxRetries && !tooLong)
                    {
                        logger.LogWarning("Graph {Method} {Path} returned {Status}; retry {Attempt} of {MaxRetries}",
                            method, StripQuery(path), status, attempt + 1, retryOptions.MaxRetries);
                        response.Dispose();
                        await DelayAsync(retryAfter, attempt, cancellationToken);
                        continue;
                    }
                }

                using (response)
                {
                    JsonElement? errorBody = null;
                    try
                    {
                        errorBody = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
                    }
                    catch (JsonException)
                    {
                        // Gateway errors can come back as HTML or empty; the status code is enough.
                    }

                    var exception = CreateException(method.Method, path, status, ReadError(errorBody));
                    throw exception is GraphThrottledException
                        ? new GraphThrottledException(exception.Message, status, exception.ErrorCode, retryAfter)
                        : exception;
                }
            }
        }

        private Task DelayAsync(TimeSpan? retryAfter, int attempt, CancellationToken cancellationToken)
        {
            var delay = retryAfter ?? TimeSpan.FromTicks(retryOptions.BaseDelay.Ticks * (1L << attempt));
            if (delay > retryOptions.MaxDelay) delay = retryOptions.MaxDelay;
            return delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;
        }

        public static GraphApiException CreateException(string method, string path, int status, (string? Code, string? Message) error)
        {
            var message = $"Graph {method} {StripQuery(path)} returned {status}{(error.Code == null ? "" : $" {error.Code}")}: {error.Message ?? "no error message"}";

            return status switch
            {
                404 => new GraphNotFoundException(message, status, error.Code),
                409 => new GraphConflictException(message, status, error.Code),
                // Assigning an app role that's already assigned is a 400 with this wording rather than a 409.
                400 when error.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true
                    => new GraphConflictException(message, status, error.Code),
                403 => new GraphForbiddenException(message, status, error.Code),
                429 or 503 => new GraphThrottledException(message, status, error.Code),
                _ => new GraphApiException(message, status, error.Code)
            };
        }

        private static (string? Code, string? Message) ReadError(JsonElement? body)
        {
            if (body is JsonElement element && element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                return (
                    error.TryGetProperty("code", out var code) ? code.GetString() : null,
                    error.TryGetProperty("message", out var message) ? message.GetString() : null);
            }
            return (null, null);
        }

        private static TimeSpan? ParseRetryAfter(string? value) =>
            int.TryParse(value, out var seconds) ? TimeSpan.FromSeconds(seconds) : null;

        private static string StripQuery(string path)
        {
            var index = path.IndexOf('?');
            return index < 0 ? path : path[..index];
        }

        private class CollectionPage<T>
        {
            public List<T>? Value { get; set; }

            [JsonPropertyName("@odata.nextLink")]
            public string? NextLink { get; set; }
        }

        private class BatchResponse
        {
            public List<BatchItem>? Responses { get; set; }
        }

        private class BatchItem
        {
            public string Id { get; set; } = string.Empty;
            public int Status { get; set; }
            public Dictionary<string, string>? Headers { get; set; }
            public JsonElement? Body { get; set; }
        }
    }
}
