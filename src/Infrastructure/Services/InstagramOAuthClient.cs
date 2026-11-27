using System.Text.Json;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Instagram;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Business Login for Instagram. The app secret and tokens travel in request bodies and query strings as Meta
    /// expects, so request URIs must never be logged: the HttpClient is registered without the factory's request
    /// loggers, and exception messages here are built from the response body only.
    /// </summary>
    public class InstagramOAuthClient(HttpClient httpClient, IOptions<InstagramSettings> options) : IInstagramOAuthClient
    {
        private const string AuthorizationEndpoint = "https://www.instagram.com/oauth/authorize";
        private const string CodeEndpoint = "https://api.instagram.com/oauth/access_token";
        private const string LongLivedEndpoint = "https://graph.instagram.com/access_token";

        public bool CanAuthorize =>
            !string.IsNullOrWhiteSpace(options.Value.AppId)
            && !string.IsNullOrWhiteSpace(options.Value.AppSecret)
            && !string.IsNullOrWhiteSpace(options.Value.RedirectUri);

        public string AuthorizationUrl(string state)
        {
            var settings = options.Value;
            KeyValuePair<string, string>[] parameters =
            [
                new("client_id", settings.AppId),
                new("redirect_uri", settings.RedirectUri),
                new("response_type", "code"),
                new("scope", InstagramPermissions.Basic),
                // Instagram asks who is signing in even when the browser already has a session, so the photographer
                // picks the business account and not whichever personal one happens to be signed in
                new("force_reauth", "true"),
                new("state", state),
            ];

            return AuthorizationEndpoint + "?" + string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        }

        public async Task<InstagramAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            var settings = options.Value;

            // The code is good for an hour, once. Instagram's redirect ends in "#_", which is not part of it.
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.AppId,
                ["client_secret"] = settings.AppSecret,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = settings.RedirectUri,
                ["code"] = code.Trim() is var trimmed && trimmed.EndsWith("#_") ? trimmed[..^2] : trimmed,
            });
            using var shortLived = await SendAsync(new HttpRequestMessage(HttpMethod.Post, CodeEndpoint) { Content = form }, cancellationToken);

            // Documented as { "data": [ { ... } ] }; also seen without the wrapper
            var granted = shortLived.RootElement;
            if (granted.ValueKind == JsonValueKind.Object && granted.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                granted = data[0];
            }

            var shortToken = Text(granted, "access_token");
            if (string.IsNullOrWhiteSpace(shortToken))
            {
                throw new InstagramApiException("Instagram's response to the sign-in code did not include a token.");
            }

            // The short-lived token lasts an hour; the feed needs the 60 day one, which can then be refreshed
            using var longLived = await SendAsync(new HttpRequestMessage(HttpMethod.Get,
                $"{LongLivedEndpoint}?grant_type=ig_exchange_token&client_secret={Uri.EscapeDataString(settings.AppSecret)}"
                    + $"&access_token={Uri.EscapeDataString(shortToken)}"), cancellationToken);

            var token = Text(longLived.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InstagramApiException("Instagram's long-lived token response did not include a token.");
            }

            var expiresIn = longLived.RootElement.TryGetProperty("expires_in", out var seconds)
                && seconds.ValueKind == JsonValueKind.Number && seconds.TryGetInt64(out var value) && value > 0
                    ? TimeSpan.FromSeconds(value)
                    : (TimeSpan?)null;

            return new InstagramAuthorization(token, expiresIn, Permissions(granted));
        }

        private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new InstagramApiException($"Instagram could not be reached ({ex.GetType().Name}).", innerException: ex);
            }
            finally
            {
                request.Dispose();
            }

            using (response)
            {
                JsonDocument? body = null;
                try
                {
                    body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                }
                catch (JsonException) when (!response.IsSuccessStatusCode)
                {
                    // Reported by status below
                }
                catch (JsonException ex)
                {
                    throw new InstagramApiException("Instagram returned an unexpected response.", (int)response.StatusCode, innerException: ex);
                }

                if (response.IsSuccessStatusCode && body!.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return body;
                }

                using (body)
                {
                    throw new InstagramApiException($"Instagram returned {(int)response.StatusCode}: {ErrorMessage(body) ?? response.ReasonPhrase}",
                        (int)response.StatusCode);
                }
            }
        }

        /// <summary>
        /// The code endpoint answers { error_type, code, error_message }; graph.instagram.com answers { error: { message } }
        /// </summary>
        private static string? ErrorMessage(JsonDocument? body)
        {
            if (body == null || body.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = body.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                return Text(error, "message");
            }

            return Text(root, "error_message");
        }

        /// <summary>
        /// Documented as one comma-separated string; also seen as a list
        /// </summary>
        private static IReadOnlyList<string> Permissions(JsonElement granted)
        {
            if (granted.ValueKind != JsonValueKind.Object || !granted.TryGetProperty("permissions", out var permissions))
            {
                return [];
            }

            return permissions.ValueKind switch
            {
                JsonValueKind.String => permissions.GetString()!.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                JsonValueKind.Array => permissions.EnumerateArray()
                    .Where(p => p.ValueKind == JsonValueKind.String)
                    .Select(p => p.GetString()!)
                    .ToList(),
                _ => [],
            };
        }

        private static string? Text(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
