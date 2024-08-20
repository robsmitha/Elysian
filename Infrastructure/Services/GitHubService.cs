using Elysian.Application.Features.Code.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Responses.GitHub;
using Elysian.Domain.Security;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Text;

namespace Elysian.Infrastructure.Services
{
    public class GitHubService(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IGitHubService
    {
        public async Task<GitHubOAuthUrl> GetGitHubOAuthUrlAsync(string state)
        {
            var urlBuilder = new StringBuilder();
            urlBuilder.Append("https://github.com/login/oauth/authorize?");
            urlBuilder.Append($"allow_signup=true");
            urlBuilder.Append($"&client_id={configuration.GetValue<string>("GitHub:ClientId")}");
            urlBuilder.Append($"&redirect_uri={configuration.GetValue<string>("GitHub:SuccessRedirectUri")}");
            urlBuilder.Append($"&state={OAuthStateEncryptor.ComputeHash(configuration.GetValue<string>("SecretKey"), state)}");
            return new GitHubOAuthUrl(urlBuilder.ToString());
        }

        public async Task<GitHubAccessTokenResponse> GetGitHubAccessTokenAsync(GitHubAccessTokenRequest requestAccessToken)
        {
            var content = new StringContent(JsonConvert.SerializeObject(new
            {
                requestAccessToken?.code,
                client_id = configuration.GetValue<string>("GitHub:ClientId"),
                client_secret = configuration.GetValue<string>("GitHub:Secret")
            }), Encoding.UTF8, "application/json");

            var client = httpClientFactory.CreateClient("GitHubOAuth");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            var response = await client.PostAsync("/login/oauth/access_token", content);
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GitHubAccessTokenResponse>(json) ?? throw new Exception("Empty Access Token.");
        }

        public async Task<GitHubCodeSearchResponse> GetGitHubCodeSearchResultsAsync(GitHubCodeSearchRequest requestCodeSearch)
        {
            var urlBuilder = new StringBuilder();
            urlBuilder.Append("/search/code");
            urlBuilder.Append($"?q={requestCodeSearch.Term} user:robsmitha");

            var client = httpClientFactory.CreateClient("GitHubApi");
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, urlBuilder.ToString());
            httpRequestMessage.Headers.Add("Accept", "application/vnd.github+json,application/vnd.github.text-match+json");
            if (!string.IsNullOrEmpty(requestCodeSearch.AccessToken))
            {
                httpRequestMessage.Headers.Add("Authorization", $"Bearer {requestCodeSearch.AccessToken}");
            }

            var response = await client.SendAsync(httpRequestMessage);

            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GitHubCodeSearchResponse>(json);
        }

        public async Task<string> GetRepositoryContentsAsHtmlAsync(GitHubRepositoryContentsRequest requestRepoContents)
        {
            var urlBuilder = new StringBuilder();
            urlBuilder.Append("/repos");
            urlBuilder.Append($"/{requestRepoContents.Username}");
            urlBuilder.Append($"/{requestRepoContents.RepoName}");
            urlBuilder.Append("/contents");
            urlBuilder.Append($"/{requestRepoContents.Path}");

            var client = httpClientFactory.CreateClient("GitHubApi");
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, urlBuilder.ToString());
            httpRequestMessage.Headers.Add("Accept", "application/vnd.github+json,application/vnd.github.html+json");
            if (!string.IsNullOrEmpty(requestRepoContents.AccessToken))
            {
                httpRequestMessage.Headers.Add("Authorization", $"Bearer {requestRepoContents.AccessToken}");
            }

            var response = await client.SendAsync(httpRequestMessage);

            var html = await response.Content.ReadAsStringAsync();

            return html;
        }
    }
}
