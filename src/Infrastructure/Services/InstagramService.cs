using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Instagram.Models;
using Elysian.Application.Interfaces;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Instagram API with Instagram Login. The token travels in the query string as the API expects, so request
    /// URIs must never be logged: the HttpClient is registered without the factory's request loggers, and
    /// exception messages here are built from the response body only.
    /// </summary>
    public partial class InstagramService(HttpClient httpClient, IInstagramTokenStore tokenStore, TimeProvider timeProvider)
        : IInstagramService
    {
        private const string MediaFields = "id,caption,media_type,media_url,thumbnail_url,permalink,timestamp";

        public async Task<List<InstagramPostModel>> GetRecentPostsAsync(int count, CancellationToken cancellationToken = default)
        {
            var token = await GetTokenAsync(cancellationToken);
            var limit = Math.Clamp(count, 1, 100);

            var response = await SendAsync<MediaResponse>(
                $"me/media?fields={MediaFields}&limit={limit}&access_token={Uri.EscapeDataString(token.AccessToken)}", cancellationToken);

            return (response.Data ?? [])
                .Select(ToPostModel)
                .OfType<InstagramPostModel>()
                .ToList();
        }

        public async Task<InstagramToken> RefreshTokenAsync(CancellationToken cancellationToken = default)
        {
            var token = await GetTokenAsync(cancellationToken);

            var response = await SendAsync<RefreshResponse>(
                $"refresh_access_token?grant_type=ig_refresh_token&access_token={Uri.EscapeDataString(token.AccessToken)}", cancellationToken);

            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                throw new InstagramApiException("Instagram's token refresh response did not include a token.");
            }

            var refreshed = new InstagramToken(response.AccessToken, timeProvider.GetUtcNow());
            await tokenStore.SaveAsync(refreshed, cancellationToken);
            return refreshed;
        }

        private async Task<InstagramToken> GetTokenAsync(CancellationToken cancellationToken) =>
            await tokenStore.GetAsync(cancellationToken)
                ?? throw new InstagramApiException("No Instagram access token is configured (Instagram:AccessToken).");

        private async Task<T> SendAsync<T>(string relativeUri, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(relativeUri, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new InstagramApiException($"Instagram could not be reached ({ex.GetType().Name}).", innerException: ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var error = await ReadErrorAsync(response, cancellationToken);
                    throw new InstagramApiException(
                        $"Instagram returned {(int)response.StatusCode}: {error?.Type ?? "UnknownError"} (code {error?.Code?.ToString() ?? "none"}"
                            + $"{(error?.Subcode is int subcode ? $", subcode {subcode}" : "")}): {error?.Message ?? response.ReasonPhrase}",
                        (int)response.StatusCode, error?.Code);
                }

                try
                {
                    return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                        ?? throw new InstagramApiException("Instagram returned an empty response.", (int)response.StatusCode);
                }
                catch (JsonException ex)
                {
                    throw new InstagramApiException("Instagram returned an unexpected response.", (int)response.StatusCode, innerException: ex);
                }
            }
        }

        private static async Task<GraphError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                return (await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken))?.Error;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>
        /// Videos show their thumbnail; images and albums (whose media_url is the first item) show the media itself.
        /// Posts with neither, e.g. a video whose thumbnail Instagram withheld, are skipped.
        /// </summary>
        private static InstagramPostModel? ToPostModel(MediaItem item)
        {
            var imageUrl = item.MediaType == "VIDEO" ? item.ThumbnailUrl : item.MediaUrl;
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(item.Permalink))
            {
                return null;
            }

            return new InstagramPostModel(item.Id, imageUrl, item.Permalink, item.Caption, item.MediaType ?? "IMAGE", ParseTimestamp(item.Timestamp));
        }

        /// <summary>
        /// Instagram sends offsets without a colon ("2024-05-01T14:03:00+0000"), which DateTimeOffset won't parse as is
        /// </summary>
        private static DateTimeOffset? ParseTimestamp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = CompactOffset().Replace(value, "$1:$2");
            return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
                ? timestamp
                : null;
        }

        [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
        private static partial Regex CompactOffset();

        private class MediaResponse
        {
            [JsonPropertyName("data")]
            public List<MediaItem>? Data { get; set; }
        }

        private class MediaItem
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("caption")]
            public string? Caption { get; set; }

            [JsonPropertyName("media_type")]
            public string? MediaType { get; set; }

            [JsonPropertyName("media_url")]
            public string? MediaUrl { get; set; }

            [JsonPropertyName("thumbnail_url")]
            public string? ThumbnailUrl { get; set; }

            [JsonPropertyName("permalink")]
            public string? Permalink { get; set; }

            [JsonPropertyName("timestamp")]
            public string? Timestamp { get; set; }
        }

        private class RefreshResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("expires_in")]
            public long? ExpiresIn { get; set; }
        }

        private class ErrorResponse
        {
            [JsonPropertyName("error")]
            public GraphError? Error { get; set; }
        }

        private class GraphError
        {
            [JsonPropertyName("message")]
            public string? Message { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("code")]
            public int? Code { get; set; }

            [JsonPropertyName("error_subcode")]
            public int? Subcode { get; set; }
        }
    }
}
