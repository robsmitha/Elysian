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

        /// <summary>
        /// Fetches whatever checking a token needs from the sign-in provider, so the first person to sign in
        /// doesn't wait for it. A host may call this at startup. Does nothing when sign-in isn't configured.
        /// </summary>
        Task WarmUpAsync(CancellationToken cancellationToken = default);
    }

    public static class SignInClaims
    {
        public const string IdentityProvider = "idp";
        public const string ExternalIdProvider = "externalid";
    }
}
