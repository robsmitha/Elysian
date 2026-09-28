using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Verifies Cloudflare Turnstile tokens with the siteverify endpoint
    /// </summary>
    public class TurnstileVerificationService(HttpClient httpClient, IOptions<TurnstileSettings> options,
        ILogger<TurnstileVerificationService> logger) : IHumanVerificationService
    {
        private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

        public async Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(options.Value.SecretKey))
            {
                throw new InvalidOperationException($"{TurnstileSettings.SectionName}:{nameof(TurnstileSettings.SecretKey)} is not configured.");
            }

            var form = new Dictionary<string, string>
            {
                ["secret"] = options.Value.SecretKey,
                ["response"] = token,
            };
            if (!string.IsNullOrEmpty(remoteIp))
            {
                form["remoteip"] = remoteIp;
            }

            try
            {
                using var response = await httpClient.PostAsync(SiteVerifyUrl, new FormUrlEncodedContent(form), cancellationToken);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
                if (result?.Success == true)
                {
                    return true;
                }

                logger.LogWarning("Turnstile verification failed: {ErrorCodes}", string.Join(", ", result?.ErrorCodes ?? []));
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Fail closed: without a verdict from Cloudflare the submission is treated as unverified
                logger.LogError(ex, "Turnstile siteverify request failed");
                return false;
            }
        }

        private class SiteVerifyResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("error-codes")]
            public string[]? ErrorCodes { get; set; }
        }
    }
}
