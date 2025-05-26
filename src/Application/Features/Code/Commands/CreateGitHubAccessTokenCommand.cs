using Elysian.Application.Features.Code.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;

namespace Elysian.Application.Features.Code.Commands
{
    [Authorize(Policy = PolicyNames.CodeWrite)]
    public record CreateGitHubAccessTokenCommand(GitHubAccessTokenRequest GitHubAccessTokenRequest) : IRequest<OAuthToken>;

    public class CreateGitHubAccessTokenCommandValidator : AbstractValidator<CreateGitHubAccessTokenCommand>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        private readonly GitHubSettings _gitHubSettings;
        public CreateGitHubAccessTokenCommandValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor,
            IOptions<GitHubSettings> options)
        {
            _context = context;
            _claimsPrincipalAccessor = claimsPrincipalAccessor;
            _gitHubSettings = options.Value;

            RuleFor(v => v.GitHubAccessTokenRequest)
                .NotEmpty()
                .MustAsync(MustProvideParameters)
                    .WithMessage("Code and State parameters must be provided. Please try again.");

            RuleFor(v => v.GitHubAccessTokenRequest)
                .NotEmpty()
                .MustAsync(MustProvideValidState)
                    .WithMessage("Invalid state provided. Please try again.");
        }

        public async Task<bool> MustProvideParameters(GitHubAccessTokenRequest accessTokenRequest,
            CancellationToken cancellationToken)
        {
            return !string.IsNullOrWhiteSpace(accessTokenRequest?.code) && !string.IsNullOrWhiteSpace(accessTokenRequest?.state);
        }

        public async Task<bool> MustProvideValidState(GitHubAccessTokenRequest accessTokenRequest,
            CancellationToken cancellationToken)
        {
            var oAuthState = await _context.OAuthStates.OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(s => s.UserId == _claimsPrincipalAccessor.UserId && s.OAuthProvider == OAuthProviders.GitHub);

            return !string.IsNullOrWhiteSpace(accessTokenRequest?.state) && OAuthStateEncryptor.Validate(_gitHubSettings.Secret, oAuthState.State, accessTokenRequest.state);
        }
    }

    public class CreateGitHubAccessTokenCommandHandler(ElysianContext context, IGitHubService gitHubService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<CreateGitHubAccessTokenCommand, OAuthToken>
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
