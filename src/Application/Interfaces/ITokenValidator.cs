using System.Security.Claims;

namespace Elysian.Application.Interfaces
{
    public interface ITokenValidator
    {
        /// <summary>
        /// The signed-in person a bearer token proves, in the shape the rest of Elysian reads (name identifier,
        /// name, email and "idp" claims), or null when the token is missing or doesn't check out.
        /// </summary>
        Task<ClaimsPrincipal?> ValidateAsync(string? bearerToken, CancellationToken cancellationToken = default);
    }

    public static class SignInClaims
    {
        public const string IdentityProvider = "idp";
        public const string ExternalIdProvider = "externalid";
    }
}
