using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
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
    [Authorize(Policy = PolicyNames.CodeRead)]
    public record GetGitHubAccessTokenQuery : IRequest<OAuthToken>;

    public class GetGitHubAccessTokenQueryHandler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<GetGitHubAccessTokenQuery, OAuthToken>
    {
        public async Task<OAuthToken> Handle(GetGitHubAccessTokenQuery request, CancellationToken cancellationToken)
        {
            return await context.OAuthTokens.SingleOrDefaultAsync(t => t.UserId == claimsPrincipalAccessor.UserId && t.OAuthProvider == OAuthProviders.GitHub, cancellationToken: cancellationToken);
        }
    }
}
