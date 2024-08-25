using Elysian.Application.Features.Code.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Identity;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Code.Commands
{
    public class CreateGitHubOAuthUrlCommand : IRequest<GitHubOAuthUrl>
    {
        public class Handler(ElysianContext context, IGitHubService gitHubService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<CreateGitHubOAuthUrlCommand, GitHubOAuthUrl>
        {
            public async Task<GitHubOAuthUrl> Handle(CreateGitHubOAuthUrlCommand request, CancellationToken cancellationToken)
            {
                var oAuthState = new OAuthState
                {
                    State = Guid.NewGuid().ToString(),
                    OAuthProvider = OAuthProviders.GitHub,
                    UserId = claimsPrincipalAccessor.UserId,
                    CreatedAt = DateTime.UtcNow
                };

                await context.AddAsync(oAuthState);
                await context.SaveChangesAsync();

                return await gitHubService.GetGitHubOAuthUrlAsync(oAuthState.State);
            }
        }
    }
}
