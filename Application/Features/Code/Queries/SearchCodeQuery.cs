using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Responses.GitHub;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Code.Queries
{
    public class SearchCodeQuery(string term) : IRequest<GitHubCodeSearchResponse>
    {
        public string Term { get; set; } = term;

        public class Handler(ElysianContext context, IGitHubService gitHubService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SearchCodeQuery, GitHubCodeSearchResponse>
        {
            public async Task<GitHubCodeSearchResponse> Handle(SearchCodeQuery request, CancellationToken cancellationToken)
            {
                var userGitHubTokenQuery = context.OAuthTokens.Where(t => t.UserId == claimsPrincipalAccessor.UserId && t.OAuthProvider == OAuthProviders.GitHub);

                var accessToken = claimsPrincipalAccessor.IsAuthenticated && await userGitHubTokenQuery.AnyAsync()
                    ? await userGitHubTokenQuery.Select(t => t.AccessToken).SingleOrDefaultAsync()
                    : null;

                return await gitHubService.GetGitHubCodeSearchResultsAsync(new GitHubCodeSearchRequest
                {
                    Term = request.Term,
                    AccessToken = accessToken
                });
            }
        }
    }
}
