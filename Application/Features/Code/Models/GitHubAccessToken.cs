using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Code.Models
{
    public record GitHubOAuthUrl(string url);
    public record GitHubAccessTokenRequest(string state, string code);
    public record GitHubAccessTokenResponse(string token_type, string access_token, string? scope);
}
