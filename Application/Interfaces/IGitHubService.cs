using Elysian.Application.Features.Code.Models;
using Elysian.Domain.Responses.GitHub;

namespace Elysian.Application.Interfaces
{
    public interface IGitHubService
    {
        Task<GitHubOAuthUrl> GetGitHubOAuthUrlAsync(string state);
        Task<GitHubAccessTokenResponse> GetGitHubAccessTokenAsync(GitHubAccessTokenRequest requestAccessToken);
        Task<GitHubCodeSearchResponse> GetGitHubCodeSearchResultsAsync(GitHubCodeSearchRequest requestCodeSearch);
        Task<string> GetRepositoryContentsAsHtmlAsync(GitHubRepositoryContentsRequest requestRepoContents);
    }
}
