using System.Security.Claims;
using System.Security.Cryptography;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Identity;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Elysian.Tests.Platforms
{
    public class JwtTokenValidatorTests
    {
        private const string Issuer = "https://login.example.com/tenant/v2.0";
        private const string Audience = "api-app-id";

        private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "key-1" };
        private static readonly RsaSecurityKey SomeoneElsesKey = new(RSA.Create(2048)) { KeyId = "key-1" };

        [Fact]
        public async Task A_valid_token_becomes_a_principal_in_the_shape_elysian_reads()
        {
            var token = Token(new() { ["oid"] = "object-id", ["sub"] = "pairwise-sub", ["email"] = "Owner@Example.com" });

            var principal = await Validator().ValidateAsync($"Bearer {token}");

            Assert.NotNull(principal);
            Assert.True(principal!.Identity!.IsAuthenticated);
            Assert.Equal("object-id", principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            Assert.Equal("owner@example.com", principal.FindFirst(ClaimTypes.Email)!.Value);
            Assert.Equal("owner@example.com", principal.FindFirst(ClaimTypes.Name)!.Value);
            Assert.Equal(SignInClaims.ExternalIdProvider, principal.FindFirst(SignInClaims.IdentityProvider)!.Value);
        }

        [Fact]
        public async Task Roles_in_the_token_are_ignored()
        {
            var token = Token(new() { ["sub"] = "person", ["roles"] = "systemAdmin" });

            var principal = await Validator().ValidateAsync(token);

            Assert.Equal("person", principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            Assert.Empty(principal.FindAll(ClaimTypes.Role));
        }

        [Fact]
        public async Task Tokens_that_dont_check_out_are_refused()
        {
            var validator = Validator();
            var claims = new Dictionary<string, object> { ["sub"] = "person" };

            Assert.Null(await validator.ValidateAsync(null));
            Assert.Null(await validator.ValidateAsync("Bearer "));
            Assert.Null(await validator.ValidateAsync("Bearer not-a-token"));
            Assert.Null(await validator.ValidateAsync(Token(claims, audience: "another-api")));
            Assert.Null(await validator.ValidateAsync(Token(claims, issuer: "https://evil.example.com")));
            Assert.Null(await validator.ValidateAsync(Token(claims, expires: DateTime.UtcNow.AddMinutes(-10))));
            Assert.Null(await validator.ValidateAsync(Token(claims, key: SomeoneElsesKey)));
            Assert.Null(await validator.ValidateAsync(Token(new() { ["name"] = "no subject" })));
        }

        [Fact]
        public async Task Unconfigured_sign_in_fails_the_request_with_the_settings_named()
        {
            var validator = new JwtTokenValidator(Options.Create(new AuthSettings()), NullLogger<JwtTokenValidator>.Instance);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync("Bearer anything"));

            Assert.Contains("Auth:Authority", exception.Message);
        }

        [Fact]
        public async Task Warming_up_fetches_the_providers_keys_before_anyone_signs_in()
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(SigningKey);
            var manager = new CountingConfigurationManager(configuration);
            var validator = new JwtTokenValidator(
                Options.Create(new AuthSettings { Authority = "https://login.example.com/tenant/v2.0", Audience = Audience }),
                NullLogger<JwtTokenValidator>.Instance, manager);

            await validator.WarmUpAsync();

            Assert.Equal(1, manager.Fetches);
            Assert.NotNull(await validator.ValidateAsync(Token(new() { ["sub"] = "person" })));
        }

        [Fact]
        public async Task Warming_up_does_nothing_when_sign_in_is_not_configured()
        {
            var validator = new JwtTokenValidator(Options.Create(new AuthSettings()), NullLogger<JwtTokenValidator>.Instance);

            await validator.WarmUpAsync();
        }

        private sealed class CountingConfigurationManager(OpenIdConnectConfiguration configuration) : IConfigurationManager<OpenIdConnectConfiguration>
        {
            public int Fetches { get; private set; }

            public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
            {
                Fetches++;
                return Task.FromResult(configuration);
            }

            public void RequestRefresh()
            {
            }
        }

        private static JwtTokenValidator Validator()
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(SigningKey);

            return new JwtTokenValidator(
                Options.Create(new AuthSettings { Authority = "https://login.example.com/tenant/v2.0", Audience = Audience }),
                NullLogger<JwtTokenValidator>.Instance,
                new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
        }

        private static string Token(Dictionary<string, object> claims, string issuer = Issuer, string audience = Audience,
            DateTime? expires = null, SecurityKey? key = null) =>
            new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Claims = claims,
                NotBefore = (expires ?? DateTime.UtcNow.AddMinutes(30)).AddHours(-1),
                Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
                SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
            });
    }
}
