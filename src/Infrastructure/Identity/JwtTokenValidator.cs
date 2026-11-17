using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace Elysian.Infrastructure.Identity
{
    /// <summary>
    /// Checks a sign-in token against the provider's published signing keys. Works with any OpenID Connect
    /// provider; the platform uses Microsoft Entra External ID.
    /// </summary>
    public class JwtTokenValidator(IOptions<AuthSettings> options, ILogger<JwtTokenValidator> logger,
        IConfigurationManager<OpenIdConnectConfiguration>? configurationManager = null) : ITokenValidator
    {
        private static readonly JsonWebTokenHandler handler = new() { MapInboundClaims = false };

        private readonly object gate = new();
        private IConfigurationManager<OpenIdConnectConfiguration>? manager = configurationManager;

        public async Task<ClaimsPrincipal?> ValidateAsync(string? bearerToken, CancellationToken cancellationToken = default)
        {
            var token = StripScheme(bearerToken);
            if (token == null)
            {
                return null;
            }

            var settings = options.Value;
            if (string.IsNullOrWhiteSpace(settings.Authority) || string.IsNullOrWhiteSpace(settings.Audience))
            {
                throw new InvalidOperationException(
                    $"{AuthSettings.SectionName}:{nameof(AuthSettings.Authority)} and {AuthSettings.SectionName}:{nameof(AuthSettings.Audience)} are not configured, so sign-in tokens can't be checked.");
            }

            var result = await ValidateAsync(token, settings, cancellationToken);
            if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                // The provider may have rotated its keys since they were last fetched
                Manager(settings).RequestRefresh();
                result = await ValidateAsync(token, settings, cancellationToken);
            }

            if (!result.IsValid)
            {
                logger.LogWarning("Sign-in token rejected: {Reason}", result.Exception?.GetType().Name);
                return null;
            }

            return Normalize(result.ClaimsIdentity);
        }

        private async Task<TokenValidationResult> ValidateAsync(string token, AuthSettings settings, CancellationToken cancellationToken)
        {
            var configuration = await Manager(settings).GetConfigurationAsync(cancellationToken);

            return await handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidIssuer = configuration.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKeys = configuration.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            });
        }

        private IConfigurationManager<OpenIdConnectConfiguration> Manager(AuthSettings settings)
        {
            lock (gate)
            {
                return manager ??= new ConfigurationManager<OpenIdConnectConfiguration>(
                    $"{settings.Authority!.TrimEnd('/')}/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever { RequireHttps = true });
            }
        }

        private static string? StripScheme(string? bearerToken)
        {
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                return null;
            }

            var value = bearerToken.Trim();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                value = value["Bearer ".Length..].Trim();
            }

            return value.Length == 0 ? null : value;
        }

        /// <summary>
        /// "oid" is the person's id across the whole directory; "sub" differs per application, so it's only the fallback
        /// </summary>
        private static ClaimsPrincipal? Normalize(ClaimsIdentity token)
        {
            var subject = token.FindFirst("oid")?.Value ?? token.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                return null;
            }

            var email = (token.FindFirst("email")?.Value ?? token.FindFirst("preferred_username")?.Value ?? token.FindFirst("emails")?.Value)
                ?.Trim().ToLowerInvariant();
            var hasEmail = !string.IsNullOrEmpty(email) && email.Contains('@');

            var identity = new ClaimsIdentity(SignInClaims.ExternalIdProvider);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
            identity.AddClaim(new Claim(ClaimTypes.Name, hasEmail ? email! : token.FindFirst("name")?.Value ?? subject));
            identity.AddClaim(new Claim(SignInClaims.IdentityProvider, SignInClaims.ExternalIdProvider));
            if (hasEmail)
            {
                identity.AddClaim(new Claim(ClaimTypes.Email, email!));
            }

            return new ClaimsPrincipal(identity);
        }
    }
}
