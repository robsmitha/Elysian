using Elysian.Application.Features.Code.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Code.Commands
{
    public class CreateGitHubAccessTokenCommand(GitHubAccessTokenRequest gitHubAccessTokenRequest) : IRequest<OAuthToken>
    {
        public GitHubAccessTokenRequest GitHubAccessTokenRequest { get; set; } = gitHubAccessTokenRequest;

        public class Handler(ElysianContext context, IGitHubService gitHubService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<CreateGitHubAccessTokenCommand, OAuthToken>
        {
            public async Task<OAuthToken> Handle(CreateGitHubAccessTokenCommand request, CancellationToken cancellationToken)
            {
                var oAuthState = await context.OAuthStates.OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync(s => s.UserId == claimsPrincipalAccessor.UserId && s.OAuthProvider == OAuthProviders.GitHub);

                var accessToken = await gitHubService.GetGitHubAccessTokenAsync(request.GitHubAccessTokenRequest);

                var existingOAuthToken = await context.OAuthTokens.SingleOrDefaultAsync(t => t.UserId == claimsPrincipalAccessor.UserId && t.OAuthProvider == OAuthProviders.GitHub);
                var oAuthToken = new OAuthToken
                {
                    AccessToken = accessToken.access_token,
                    OAuthProvider = "github",
                    TokenType = accessToken.token_type,
                    Scope = accessToken.scope,
                    UserId = claimsPrincipalAccessor.UserId
                };

                if (existingOAuthToken != null)
                {
                    context.Remove(existingOAuthToken);
                }
                context.Remove(oAuthState);
                await context.AddAsync(oAuthToken);
                await context.SaveChangesAsync();
                return oAuthToken;
            }
        }
    }
}
