using Elysian.Application.Features.Code.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using MediatR;

namespace Elysian.Application.Features.Code.Commands
{
    [Authorize(Policy = PolicyNames.CodeWrite)]
    public record CreateGitHubOAuthUrlCommand : IRequest<GitHubOAuthUrl>;
    public class CreateGitHubOAuthUrlCommandHandler(ElysianContext context, IGitHubService gitHubService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<CreateGitHubOAuthUrlCommand, GitHubOAuthUrl>
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
