using System.Security.Claims;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Identity;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elysian.Tests
{
    /// <summary>
    /// An in-memory ElysianContext for one tenant, with the auditing interceptor and a signed in principal
    /// </summary>
    public sealed class TestHost : IDisposable
    {
        public static readonly ElysianTenantInfo Tenant = new() { Id = "tenant-1", Identifier = "test", Name = "Test App" };

        private readonly ServiceProvider serviceProvider;

        public TestHost(ClaimsPrincipal? principal = null)
        {
            var services = new ServiceCollection();
            services.AddMultiTenant<ElysianTenantInfo>().WithInMemoryStore();
            serviceProvider = services.BuildServiceProvider();

            serviceProvider.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext =
                new MultiTenantContext<ElysianTenantInfo> { TenantInfo = Tenant };

            ClaimsPrincipalAccessor = new ClaimsPrincipalAccessor { Principal = principal ?? Principal("actor-swa-id", "admin@example.com") };
            TenantAccessor = serviceProvider.GetRequiredService<IMultiTenantContextAccessor<ElysianTenantInfo>>();

            var options = new DbContextOptionsBuilder<ElysianContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(new AuditableEntityInterceptor(ClaimsPrincipalAccessor, TimeProvider.System))
                .Options;

            Context = new ElysianContext(serviceProvider.GetRequiredService<IMultiTenantContextAccessor>(), options);
        }

        public ElysianContext Context { get; }
        public ClaimsPrincipalAccessor ClaimsPrincipalAccessor { get; }
        public IMultiTenantContextAccessor<ElysianTenantInfo> TenantAccessor { get; }

        public static ClaimsPrincipal Principal(string userId, string userName, string identityProvider = "aad", params string[] roles)
        {
            var identity = new ClaimsIdentity(identityProvider);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
            identity.AddClaim(new Claim(ClaimTypes.Name, userName));
            identity.AddClaim(new Claim("idp", identityProvider));
            identity.AddClaims((roles.Length == 0 ? ["authenticated"] : roles).Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(identity);
        }

        public static User User(int userId, string externalUserId, string userName, string identityProvider = "aad", params string[] policies) => new()
        {
            UserId = userId,
            ExternalUserId = externalUserId,
            UserName = userName,
            IdentityProvider = identityProvider,
            AccessControl = new AccessControl { Roles = [], Policies = [.. policies] }
        };

        public void Dispose()
        {
            Context.Dispose();
            serviceProvider.Dispose();
        }
    }
}
