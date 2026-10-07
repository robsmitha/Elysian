using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elysian.Tests.UserManagement
{
    public sealed class IdentityServiceLinkingTests : IDisposable
    {
        private readonly TestHost host = new();

        public IdentityServiceLinkingTests()
        {
            host.Context.Merchants.Add(new Merchant
            {
                MerchantIdentifier = TestHost.Tenant.Identifier!,
                Name = "Test",
                Description = "Test",
                WebsiteUrl = "https://app.example.com",
                MerchantType = new MerchantType { Name = "Test", Description = "Test" }
            });
            host.Context.SaveChanges();
        }

        private IdentityService Service() => new(host.Context, host.TenantAccessor, TimeProvider.System, NullLogger<IdentityService>.Instance);

        [Fact]
        public async Task A_new_sign_in_id_moves_a_managed_users_existing_user_and_data()
        {
            host.Context.Users.Add(TestHost.User(5, "old-swa-id", "Pat@Example.com", "aad", "code.read"));
            host.Context.Budgets.Add(new Budget { Name = "Home", UserId = "old-swa-id", StartDate = DateTime.Today, EndDate = DateTime.Today });
            host.Context.ManagedUsers.Add(Managed("pat@example.com"));
            await host.Context.SaveChangesAsync();

            var user = await Service().GetOrCreateAsync(TestHost.Principal("new-swa-id", "pat@example.com"));

            Assert.Equal(5, user.UserId);
            Assert.Equal("new-swa-id", user.ExternalUserId);
            Assert.Equal("new-swa-id", (await host.Context.Budgets.SingleAsync()).UserId);
            Assert.Equal(1, await host.Context.Users.CountAsync());

            var managed = await host.Context.ManagedUsers.SingleAsync();
            Assert.Equal(5, managed.UserId);
            Assert.Equal(InvitationStatus.Accepted, managed.InvitationStatus);
            Assert.Equal(UserAuditAction.Relinked, (await host.Context.UserAuditLogs.SingleAsync()).Action);
        }

        [Fact]
        public async Task Without_an_active_managed_user_a_new_id_gets_a_new_user()
        {
            host.Context.Users.Add(TestHost.User(5, "old-swa-id", "pat@example.com"));
            host.Context.ManagedUsers.Add(Managed("pat@example.com", AccessStatus.Revoked));
            await host.Context.SaveChangesAsync();

            var user = await Service().GetOrCreateAsync(TestHost.Principal("new-swa-id", "pat@example.com"));

            Assert.NotEqual(5, user.UserId);
            Assert.Equal("old-swa-id", (await host.Context.Users.SingleAsync(u => u.UserId == 5)).ExternalUserId);
        }

        [Fact]
        public async Task Other_providers_are_never_matched_by_email()
        {
            host.Context.Users.Add(TestHost.User(5, "old-swa-id", "pat@example.com"));
            host.Context.ManagedUsers.Add(Managed("pat@example.com"));
            await host.Context.SaveChangesAsync();

            var user = await Service().GetOrCreateAsync(TestHost.Principal("github-id", "pat@example.com", "github"));

            Assert.NotEqual(5, user.UserId);
            Assert.Null((await host.Context.ManagedUsers.SingleAsync()).UserId);
        }

        [Fact]
        public async Task First_sign_in_of_an_invited_guest_links_the_new_user()
        {
            host.Context.ManagedUsers.Add(Managed("sam@example.com"));
            await host.Context.SaveChangesAsync();

            var user = await Service().GetOrCreateAsync(TestHost.Principal("sam-swa-id", "Sam@Example.com"));

            Assert.Equal("sam@example.com", user.Email);
            var managed = await host.Context.ManagedUsers.SingleAsync();
            Assert.Equal(user.UserId, managed.UserId);
            Assert.Equal(InvitationStatus.Accepted, managed.InvitationStatus);
        }

        private static ManagedUser Managed(string email, string accessStatus = AccessStatus.Active) => new()
        {
            EntraObjectId = Guid.NewGuid().ToString(),
            Email = email,
            DisplayName = email,
            UserType = EntraUserType.Guest,
            InvitationStatus = InvitationStatus.PendingAcceptance,
            AccessStatus = accessStatus
        };

        public void Dispose() => host.Dispose();
    }
}
