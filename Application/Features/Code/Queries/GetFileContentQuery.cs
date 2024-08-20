using Elysian.Application.Features.Code.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Code.Queries
{
    public class GetFileContentQuery(string repo, string path) : IRequest<FileContentResponse>
    {
        public string Repo { get; set; } = repo;
        public string Path { get; set; } = path;

        public class Handler(ElysianContext context, IGitHubService gitHubService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
            : IRequestHandler<GetFileContentQuery, FileContentResponse>
        {
            public async Task<FileContentResponse> Handle(GetFileContentQuery request, CancellationToken cancellationToken)
            {
                var userGitHubTokenQuery = context.OAuthTokens.Where(t => t.UserId == claimsPrincipalAccessor.UserId && t.OAuthProvider == OAuthProviders.GitHub);

                var accessToken = claimsPrincipalAccessor.IsAuthenticated && await userGitHubTokenQuery.AnyAsync()
                    ? await userGitHubTokenQuery.Select(t => t.AccessToken).SingleOrDefaultAsync()
                    : null;

                var content = await gitHubService.GetRepositoryContentsAsHtmlAsync(new GitHubRepositoryContentsRequest("robsmitha", request.Repo, request.Path, accessToken));
                return new FileContentResponse(content);
            }
        }
    }
}
