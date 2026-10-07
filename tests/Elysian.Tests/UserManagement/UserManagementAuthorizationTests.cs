using System.Reflection;
using Elysian.Application.Behaviors;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.UserManagement;
using Elysian.Application.Features.UserManagement.Commands;
using Elysian.Application.Features.UserManagement.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Identity;
using MediatR;
using Moq;

namespace Elysian.Tests.UserManagement
{
    /// <summary>
    /// The admin endpoints are thin wrappers over these requests, so this is where admin access is enforced
    /// </summary>
    public class UserManagementAuthorizationTests
    {
        public static TheoryData<object> Requests() =>
        [
            new GetManagedUsersQuery(),
            new GetManagedUserQuery(1),
            new InviteUserCommand("pat@example.com", "Pat"),
            new UpdateManagedUserCommand(1, "Pat"),
            new ResendInviteCommand(1),
            new RevokeUserAccessCommand(1),
            new RestoreUserAccessCommand(1),
            new DeleteManagedUserCommand(1),
            new OnboardExistingUsersCommand(),
        ];

        [Fact]
        public void Every_user_management_request_requires_user_write()
        {
            var requestTypes = typeof(UserManagementService).Assembly.GetTypes()
                .Where(t => t.Namespace?.StartsWith("Elysian.Application.Features.UserManagement") == true
                    && t.GetInterfaces().Any(i => i == typeof(IBaseRequest)))
                .ToList();

            Assert.Equal(Requests().Count, requestTypes.Count);
            Assert.All(requestTypes, t => Assert.Contains(t.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == PolicyNames.UserWrite));
        }

        [Theory]
        [MemberData(nameof(Requests))]
        public async Task Users_without_user_write_are_forbidden(object request)
        {
            var (behaviorResult, nextCalled) = await RunAsync(request, TestHost.User(1, "swa-1", "reader@example.com", "aad", PolicyNames.UserRead));

            await Assert.ThrowsAsync<ForbiddenAccessException>(behaviorResult);
            Assert.False(nextCalled());
        }

        [Theory]
        [MemberData(nameof(Requests))]
        public async Task Signed_out_callers_are_unauthorized(object request)
        {
            var (behaviorResult, nextCalled) = await RunAsync(request, user: null);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(behaviorResult);
            Assert.False(nextCalled());
        }

        [Fact]
        public async Task Admins_with_user_write_are_allowed()
        {
            var (behaviorResult, nextCalled) = await RunAsync(new InviteUserCommand("pat@example.com", "Pat"),
                TestHost.User(1, "swa-1", "admin@example.com", "aad", PolicyNames.UserRead, PolicyNames.UserWrite));

            await behaviorResult();
            Assert.True(nextCalled());
        }

        [Fact]
        public async Task Hard_delete_also_requires_user_delete()
        {
            var (behaviorResult, _) = await RunAsync(new DeleteManagedUserCommand(1),
                TestHost.User(1, "swa-1", "admin@example.com", "aad", PolicyNames.UserWrite));
            await Assert.ThrowsAsync<ForbiddenAccessException>(behaviorResult);

            var (allowed, nextCalled) = await RunAsync(new DeleteManagedUserCommand(1),
                TestHost.User(1, "swa-1", "admin@example.com", "aad", PolicyNames.UserWrite, PolicyNames.UserDelete));
            await allowed();
            Assert.True(nextCalled());
        }

        private static Task<(Func<Task> Run, Func<bool> NextCalled)> RunAsync(object request, User? user)
        {
            var accessor = new ClaimsPrincipalAccessor
            {
                Principal = user == null ? new System.Security.Claims.ClaimsPrincipal() : TestHost.Principal(user.ExternalUserId, user.UserName)
            };
            var identity = new Mock<IIdentityService>();
            if (user != null)
            {
                identity.Setup(i => i.GetOrCreateAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);
                identity.Setup(i => i.AuthorizeAsync(user, It.IsAny<string>()))
                    .ReturnsAsync((User u, string policy) => u.AccessControl.Policies.Contains(policy));
            }

            var called = false;
            var method = typeof(UserManagementAuthorizationTests)
                .GetMethod(nameof(Invoke), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(request.GetType());

            Func<Task> run = () => (Task)method.Invoke(null, [request, accessor, identity.Object, (Action)(() => called = true)])!;
            return Task.FromResult((run, (Func<bool>)(() => called)));
        }

        private static async Task Invoke<TRequest>(TRequest request, IClaimsPrincipalAccessor accessor, IIdentityService identity, Action onNext)
            where TRequest : notnull
        {
            var behavior = new AuthorizationBehavior<TRequest, object?>(accessor, identity);
            await behavior.Handle(request, () =>
            {
                onNext();
                return Task.FromResult<object?>(null);
            }, CancellationToken.None);
        }
    }
}
