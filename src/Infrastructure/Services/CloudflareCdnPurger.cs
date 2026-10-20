using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Purge by URL: POST /zones/{zone_id}/purge_cache {"files": [...]}. The Free plan allows 100 URLs per request
    /// and 800 URLs per second (checked October 2026), so most publishes are one request.
    /// </summary>
    public class CloudflareCdnPurger(HttpClient httpClient, IOptions<CloudflareSettings> settings, ILogger<CloudflareCdnPurger> logger)
        : ICdnPurger
    {
        private const int MaxUrlsPerRequest = 100;
        private const int MaxAttempts = 4;

        private readonly CloudflareSettings _settings = settings.Value;

        public bool IsConfigured => _settings.IsConfigured;

        public async Task PurgeAsync(IReadOnlyCollection<string> urls, CancellationToken cancellationToken = default)
        {
            if (urls.Count == 0)
            {
                return;
            }

            if (!IsConfigured)
            {
                logger.LogWarning("Cloudflare is not configured; skipping purge of {Count} URLs", urls.Count);
                return;
            }

            foreach (var chunk in urls.Chunk(MaxUrlsPerRequest))
            {
                await PurgeChunkAsync(chunk, cancellationToken);
            }

            logger.LogInformation("Purged {Count} URLs from Cloudflare", urls.Count);
        }

        /// <summary>
        /// HEAD through Cloudflare: a stale cached copy shows up as an old ETag (Cloudflare passes the origin's
        /// through for images, which it doesn't compress)
        /// </summary>
        public async Task<IReadOnlyList<PublishedFile>> FindStaleAsync(IReadOnlyList<PublishedFile> files, CancellationToken cancellationToken = default)
        {
            var stale = new List<PublishedFile>();
            foreach (var file in files)
            {
                using var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, file.Url), cancellationToken);
                var served = response.Headers.ETag?.Tag;
                var cacheStatus = response.Headers.TryGetValues("cf-cache-status", out var values) ? values.FirstOrDefault() : null;

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Confirm {Url}: HTTP {Status}", file.Url, (int)response.StatusCode);
                    stale.Add(file);
                }
                else if (served != null && Normalize(served) != Normalize(file.ETag))
                {
                    logger.LogWarning("Confirm {Url}: served ETag {Served} ({CacheStatus}), expected {Expected}",
                        file.Url, served, cacheStatus, file.ETag);
                    stale.Add(file);
                }
            }

            return stale;
        }

        private async Task PurgeChunkAsync(string[] files, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"zones/{_settings.ZoneId}/purge_cache")
                {
                    Content = JsonContent.Create(new { files }),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiToken);

                TimeSpan? retryAfter = null;
                try
                {
                    using var response = await httpClient.SendAsync(request, cancellationToken);
                    var body = await ReadBodyAsync(response, cancellationToken);

                    if (response.IsSuccessStatusCode && body?.Success == true)
                    {
                        return;
                    }

                    var errors = body?.Errors is { Length: > 0 }
                        ? string.Join("; ", body.Errors.Select(e => $"{e.Code}: {e.Message}"))
                        : "no error details";

                    // 4xx other than 429 (bad token, wrong zone, bad URL) won't fix itself on retry
                    if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
                    {
                        throw new InvalidOperationException($"Cloudflare purge failed ({(int)response.StatusCode}): {errors}");
                    }

                    logger.LogWarning("Cloudflare purge attempt {Attempt} failed ({Status}): {Errors}", attempt, (int)response.StatusCode, errors);
                    retryAfter = response.Headers.RetryAfter?.Delta;
                }
                catch (HttpRequestException ex) when (attempt < MaxAttempts)
                {
                    logger.LogWarning(ex, "Cloudflare purge attempt {Attempt} failed", attempt);
                }

                await Task.Delay(retryAfter is { } delay && delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        private static bool IsTransient(HttpStatusCode status) =>
            status == HttpStatusCode.TooManyRequests || (int)status >= 500;

        private static string Normalize(string etag) => etag.Replace("W/", string.Empty).Trim('"');

        /// <summary>
        /// Null when the body isn't Cloudflare's JSON envelope (e.g. an HTML error page from a proxy)
        /// </summary>
        private static async Task<CloudflareResponse?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                return await response.Content.ReadFromJsonAsync<CloudflareResponse>(cancellationToken);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private sealed record CloudflareResponse(
            [property: JsonPropertyName("success")] bool Success,
            [property: JsonPropertyName("errors")] CloudflareError[]? Errors);

        private sealed record CloudflareError(
            [property: JsonPropertyName("code")] int Code,
            [property: JsonPropertyName("message")] string? Message);
    }
}
