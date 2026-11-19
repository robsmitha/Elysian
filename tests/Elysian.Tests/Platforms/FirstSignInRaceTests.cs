using Elysian.Application.Behaviors;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Members.Commands;
using Elysian.Application.Features.Members.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Identity;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant;
using MediatR;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
namespace Elysian.Tests.Platforms
{
    /// <summary>
    /// A page makes several API calls at once. On a person's first visit every one of them finds no user and
    /// tries to create one; the database's unique index lets a single insert through and fails the rest.
    /// The in-memory provider has no unique indexes, so the losing save is made to fail here the way SQL Server
    /// fails it, after the winning request's row is in place.
    /// </summary>
    public sealed class FirstSignInRaceTests : IDisposable
    {
        private const string Subject = "platform-admin-oid";

        private readonly LosingSave losingSave = new();
        private readonly TestHost host;
        private readonly AuthSettings settings = new() { RoleSource = RoleSource.Membership, PlatformAdminSubjects = [Subject] };

        public FirstSignInRaceTests()
        {
            host = new TestHost(interceptor: losingSave);
            host.Context.Merchants.Add(new Merchant { MerchantIdentifier = TestHost.Tenant.Identifier!, Name = "Test", Description = "Test", WebsiteUrl = "https://example.com" });
            host.Context.SaveChanges();
        }

        public void Dispose() => host.Dispose();

        [Fact]
        public async Task The_request_that_loses_the_race_uses_the_user_the_other_request_created()
        {
            losingSave.BeforeFailing = async () =>
            {
                await using var other = host.OtherContext();
                other.Users.Add(new User
                {
                    ExternalUserId = Subject,
                    UserName = "rob@example.com",
                    Email = "rob@example.com",
                    IdentityProvider = SignInClaims.ExternalIdProvider,
                    AccessControl = new AccessControl { Roles = [RoleNames.SystemAdmin], Policies = PolicyNames.GetDefaultPolicies([RoleNames.SystemAdmin]) },
                    CreatedByUserId = Subject,
                    ModifiedByUserId = Subject,
                });
                await other.SaveChangesAsync();
            };

            var identity = Identity();
            var user = await identity.GetOrCreateAsync(Person());

            Assert.Equal(Subject, user.ExternalUserId);
            Assert.True(await identity.IsInRoleAsync(user, RoleNames.SystemAdmin));

            await using var check = host.OtherContext();
            Assert.Single(await check.Users.ToListAsync());
        }

        [Fact]
        public async Task A_save_that_fails_for_another_reason_still_fails()
        {
            // Nobody else created the user, so this isn't the race and must not be swallowed
            losingSave.BeforeFailing = () => Task.CompletedTask;

            await Assert.ThrowsAsync<DbUpdateException>(() => Identity().GetOrCreateAsync(Person()));
        }

        private IdentityService Identity() =>
            new(host.Context, host.TenantAccessor, TimeProvider.System, NullLogger<IdentityService>.Instance, Options.Create(settings));

        private static ClaimsPrincipal Person()
        {
            var identity = new ClaimsIdentity(SignInClaims.ExternalIdProvider);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Subject));
            identity.AddClaim(new Claim(ClaimTypes.Name, "rob@example.com"));
            identity.AddClaim(new Claim(ClaimTypes.Email, "rob@example.com"));
            identity.AddClaim(new Claim(SignInClaims.IdentityProvider, SignInClaims.ExternalIdProvider));
            return new ClaimsPrincipal(identity);
        }

        /// <summary>
        /// Fails the next save once it's armed, after running whatever the winning request would have done
        /// </summary>
        private sealed class LosingSave : SaveChangesInterceptor
        {
            public Func<Task>? BeforeFailing { get; set; }

            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (BeforeFailing is { } winner)
                {
                    BeforeFailing = null;
                    await winner();
                    throw new DbUpdateException("Cannot insert duplicate key row in object 'dbo.User' with unique index 'AK_User_ExternalUserId'.");
                }

                return result;
            }
        }
    }
}
