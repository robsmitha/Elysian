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
    public record GetFileContentQuery(string Repo, string Path) : IRequest<FileContentResponse>;
    public class GetFileContentQueryHandler(ElysianContext context, IGitHubService gitHubService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
            : IRequestHandler<GetFileContentQuery, FileContentResponse>
    {
        public async Task<FileContentResponse> Handle(GetFileContentQuery request, CancellationToken cancellationToken)
        {
            var userGitHubTokenQuery = context.OAuthTokens.Where(t => t.UserId == claimsPrincipalAccessor.UserId && t.OAuthProvider == OAuthProviders.GitHub);

            var accessToken = claimsPrincipalAccessor.IsAuthenticated && await userGitHubTokenQuery.AnyAsync(cancellationToken: cancellationToken)
                ? await userGitHubTokenQuery.Select(t => t.AccessToken).SingleOrDefaultAsync(cancellationToken: cancellationToken)
                : null;

            var content = await gitHubService.GetRepositoryContentsAsHtmlAsync(new GitHubRepositoryContentsRequest("robsmitha", request.Repo, request.Path, accessToken));
            return new FileContentResponse(content);
        }
    }

}
