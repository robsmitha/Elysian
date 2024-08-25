using Elysian.Application.Exceptions;
using Elysian.Application.Extensions;
using Elysian.Application.Features.Code.Commands;
using Elysian.Application.Features.Code.Models;
using Elysian.Application.Features.Code.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Elysian
{
    public class CodeFunctions(IConfiguration configuration, IGitHubService gitHubService,
        IClaimsPrincipalAccessor claimsPrincipalAccessor, ElysianContext context,
        IMediator mediator)
    {
        // TODO: Protect endpoints with AuthorizationLevel.User
        // AuthorizationLevel settings do not work in development but after publishing to Azure, the authLevel setting is enforced.

        [Function("GitHubAuthMe")]
        public async Task<HttpResponseData> GitHubAuthMe([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var oAuthToken = await mediator.Send(new GetGitHubAccessTokenQuery());

            return await req.WriteJsonResponseAsync(new
            {
                HasGitHubOAuthToken = !string.IsNullOrWhiteSpace(oAuthToken?.AccessToken)
            });
        }

        [Function("GitHubSearch")]
        public async Task<HttpResponseData> GitHubSearch([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var term = req.Query["term"];

            var result = await mediator.Send(new SearchCodeQuery(term));

            return await req.WriteJsonResponseAsync(result);
        }


        [Function("GitHubRepoContents")]
        public async Task<HttpResponseData> GitHubRepoContents([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var path = req.Query["path"];
            var repo = req.Query["repo"];

            var result = await mediator.Send(new GetFileContentQuery(repo, path));

            return await req.WriteHtmlResponseAsync(result.Html);
        }

        [Function("GitHubOAuthUrl")]
        public async Task<HttpResponseData> GetGitHubOAuthUrl([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            if (!claimsPrincipalAccessor.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            var oAuthUrl = await mediator.Send(new CreateGitHubOAuthUrlCommand());

            return await req.WriteJsonResponseAsync(new
            {
                oAuthUrl = oAuthUrl.url
            });
        }

        [Function("GitHubOAuthCallback")]
        public async Task<HttpResponseData> GitHubOAuthCallback([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            if (!claimsPrincipalAccessor.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            var accessTokenRequest = await req.DeserializeBodyAsync<GitHubAccessTokenRequest>();
            if (string.IsNullOrWhiteSpace(accessTokenRequest?.code) || string.IsNullOrWhiteSpace(accessTokenRequest?.state))
            {
                throw new NotFoundException();
            }

            var oAuthState = await context.OAuthStates.OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(s => s.UserId == claimsPrincipalAccessor.UserId && s.OAuthProvider == OAuthProviders.GitHub);

            if (string.IsNullOrWhiteSpace(oAuthState?.State) || !OAuthStateEncryptor.Validate(configuration.GetValue<string>("SecretKey"), oAuthState.State, accessTokenRequest.state))
            {
                throw new NotFoundException();
            }

            var oAuthToken = await mediator.Send(new CreateGitHubAccessTokenCommand(accessTokenRequest));

            return await req.WriteJsonResponseAsync(new
            {
                HasGitHubOAuthToken = !string.IsNullOrWhiteSpace(oAuthToken?.AccessToken)
            });
        }

        [Function("CodeGeneration")]
        public async Task<HttpResponseData> GenerateCode([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var generateCodeRequest = await req.DeserializeBodyAsync<CodeGenerationRequest>();

            var codeGenerationResponse = await mediator.Send(new GenerateCodeCommand(generateCodeRequest));

            return await req.WriteJsonResponseAsync(new
            {
                code = codeGenerationResponse.Code
            });
        }
    }
}
