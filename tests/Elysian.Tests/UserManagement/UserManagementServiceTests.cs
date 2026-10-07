using Elysian.Application.Exceptions;
using Elysian.Application.Features.UserManagement;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Elysian.Tests.UserManagement
{
    public sealed class UserManagementServiceTests : IDisposable
    {
        private const string RedeemUrl = "https://login.microsoftonline.com/redeem?secret=do-not-store";

        private readonly TestHost host = new();
        private readonly Mock<IEntraDirectoryService> directory = new(MockBehavior.Strict);
        private readonly Mock<IEmailService> email = new();
        private readonly Mock<IIdentityService> identity = new();
        private readonly User actor = TestHost.User(1, "actor-swa-id", "admin@example.com");

        public UserManagementServiceTests()
        {
            identity.Setup(i => i.GetOrCreateAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(actor);
        }

        private UserManagementService Service() => new(
            host.Context, directory.Object, email.Object, identity.Object, host.ClaimsPrincipalAccessor, host.TenantAccessor,
            Options.Create(new EntraSettings { EnterpriseAppServicePrincipalId = "sp-1", AppRoleId = Guid.Empty.ToString(), AppUrl = "https://app.example.com" }),
            TimeProvider.System, NullLogger<UserManagementService>.Instance);

        private void SetupNewGuestInvite(string objectId = "oid-1")
        {
            directory.Setup(d => d.FindUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((EntraUser?)null);
            directory.Setup(d => d.InviteGuestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraInvitation(objectId, RedeemUrl, "PendingAcceptance"));
            directory.Setup(d => d.AssignAsync(objectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraAppRoleAssignment("assignment-1", objectId, Guid.Empty.ToString()));
        }

        [Fact]
        public async Task Invite_new_guest_invites_assigns_emails_and_records()
        {
            SetupNewGuestInvite();

            var result = await Service().InviteAsync("  Pat@Example.COM ", "Pat");

            Assert.True(result.Created);
            Assert.True(result.InvitationEmailSent);

            var row = await host.Context.ManagedUsers.SingleAsync();
            Assert.Equal("pat@example.com", row.Email);
            Assert.Equal("oid-1", row.EntraObjectId);
            Assert.Equal(EntraUserType.Guest, row.UserType);
            Assert.Equal(InvitationStatus.PendingAcceptance, row.InvitationStatus);
            Assert.Equal(AccessStatus.Active, row.AccessStatus);
            Assert.Equal("assignment-1", row.AppRoleAssignmentId);
            Assert.Equal("admin@example.com", row.InvitedBy);

            directory.Verify(d => d.InviteGuestAsync("pat@example.com", "Pat", It.IsAny<CancellationToken>()), Times.Once);
            email.Verify(e => e.SendAsync(It.Is<OutgoingEmail>(m =>
                m.To == "pat@example.com" && m.HtmlBody.Contains("redeem?secret=do-not-store") && m.TextBody.Contains(RedeemUrl)),
                It.IsAny<CancellationToken>()), Times.Once);

            var audit = await host.Context.UserAuditLogs.SingleAsync();
            Assert.Equal(UserAuditAction.Invited, audit.Action);
            Assert.Equal(row.ManagedUserId, audit.TargetManagedUserId);
            Assert.Equal("actor-swa-id", audit.ActorUserId);

            // The redeem URL is never persisted
            Assert.DoesNotContain("do-not-store", audit.Details ?? "");
            Assert.DoesNotContain("do-not-store", result.ToString());
        }

        [Fact]
        public async Task Invite_succeeds_when_the_assignment_already_exists()
        {
            // The directory service turns Graph's "already exists" into the existing assignment (see GraphApiClientTests)
            directory.Setup(d => d.FindUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraUser("oid-1", "Pat", "pat@example.com", null, EntraUserType.Guest, true, InvitationStatus.Accepted));
            directory.Setup(d => d.AssignAsync("oid-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraAppRoleAssignment("existing-assignment", "oid-1", Guid.Empty.ToString()));

            var result = await Service().InviteAsync("pat@example.com", "Pat");

            Assert.Equal("existing-assignment", (await host.Context.ManagedUsers.SingleAsync()).AppRoleAssignmentId);
            Assert.Equal(InvitationStatus.Accepted, result.User.InvitationStatus);

            // Already redeemed, so there's nothing to send
            Assert.False(result.InvitationEmailSent);
            directory.Verify(d => d.InviteGuestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            email.Verify(e => e.SendAsync(It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Reinviting_the_same_email_updates_the_existing_record()
        {
            SetupNewGuestInvite();
            await Service().InviteAsync("pat@example.com", "Pat");

            // Graph now knows the pending guest, and returns the same object for the new invitation
            directory.Setup(d => d.FindUserByEmailAsync("pat@example.com", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraUser("oid-1", "Pat", "pat@example.com", null, EntraUserType.Guest, true, InvitationStatus.PendingAcceptance));

            var second = await Service().InviteAsync("PAT@example.com", "Pat Smith");

            Assert.False(second.Created);
            var row = await host.Context.ManagedUsers.SingleAsync();
            Assert.Equal("Pat Smith", row.DisplayName);
            Assert.Equal(2, await host.Context.UserAuditLogs.CountAsync(a => a.Action == UserAuditAction.Invited));
            email.Verify(e => e.SendAsync(It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task Invite_of_a_tenant_member_assigns_without_an_invitation()
        {
            directory.Setup(d => d.FindUserByEmailAsync("lee@contoso.com", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraUser("member-1", "Lee", "lee@contoso.com", "lee@contoso.com", EntraUserType.Member, true, null));
            directory.Setup(d => d.AssignAsync("member-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraAppRoleAssignment("assignment-2", "member-1", Guid.Empty.ToString()));

            var result = await Service().InviteAsync("lee@contoso.com", "Lee");

            Assert.Equal(InvitationStatus.NotApplicable, result.User.InvitationStatus);
            Assert.False(result.InvitationEmailSent);
            directory.Verify(d => d.InviteGuestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Invite_keeps_the_user_when_the_email_fails()
        {
            SetupNewGuestInvite();
            email.Setup(e => e.SendAsync(It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

            var result = await Service().InviteAsync("pat@example.com", "Pat");

            Assert.False(result.InvitationEmailSent);
            Assert.Equal(AccessStatus.Active, (await host.Context.ManagedUsers.SingleAsync()).AccessStatus);
        }

        [Fact]
        public async Task Delete_refuses_tenant_members()
        {
            var row = await SeedAsync("member-1", "lee@contoso.com", EntraUserType.Member);
            directory.Setup(d => d.GetUserAsync("member-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraUser("member-1", "Lee", "lee@contoso.com", null, EntraUserType.Member, true, null));

            var ex = await Assert.ThrowsAsync<ConflictException>(() => Service().DeleteAsync(row.ManagedUserId));

            Assert.Contains("member of your organization", ex.Message);
            directory.Verify(d => d.DeleteUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            directory.Verify(d => d.RemoveAssignmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal(1, await host.Context.ManagedUsers.CountAsync());
        }

        [Fact]
        public async Task Delete_removes_a_guest_from_the_tenant_and_locally()
        {
            var row = await SeedAsync("oid-1", "pat@example.com", EntraUserType.Guest);
            directory.Setup(d => d.GetUserAsync("oid-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraUser("oid-1", "Pat", "pat@example.com", null, EntraUserType.Guest, true, InvitationStatus.Accepted));
            directory.Setup(d => d.RemoveAssignmentAsync("assignment-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            directory.Setup(d => d.FindAssignmentAsync("oid-1", It.IsAny<CancellationToken>())).ReturnsAsync((EntraAppRoleAssignment?)null);
            directory.Setup(d => d.DeleteUserAsync("oid-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await Service().DeleteAsync(row.ManagedUserId);

            Assert.Equal(0, await host.Context.ManagedUsers.CountAsync());
            Assert.Equal(UserAuditAction.Deleted, (await host.Context.UserAuditLogs.SingleAsync()).Action);
            directory.Verify(d => d.DeleteUserAsync("oid-1", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Revoke_refuses_the_signed_in_admin_by_link()
        {
            var row = await SeedAsync("oid-self", "someone-else@example.com", EntraUserType.Guest, userId: actor.UserId);

            var ex = await Assert.ThrowsAsync<ConflictException>(() => Service().RevokeAccessAsync(row.ManagedUserId));

            Assert.Equal("You can't revoke your own access.", ex.Message);
            Assert.Equal(AccessStatus.Active, (await host.Context.ManagedUsers.SingleAsync()).AccessStatus);
        }

        [Fact]
        public async Task Revoke_and_delete_refuse_the_signed_in_admin_by_email()
        {
            var row = await SeedAsync("oid-self", "admin@example.com", EntraUserType.Guest);

            await Assert.ThrowsAsync<ConflictException>(() => Service().RevokeAccessAsync(row.ManagedUserId));
            await Assert.ThrowsAsync<ConflictException>(() => Service().DeleteAsync(row.ManagedUserId));
        }

        [Fact]
        public async Task Revoke_removes_the_assignment_and_keeps_the_guest()
        {
            var row = await SeedAsync("oid-1", "pat@example.com", EntraUserType.Guest);
            directory.Setup(d => d.RemoveAssignmentAsync("assignment-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            directory.Setup(d => d.FindAssignmentAsync("oid-1", It.IsAny<CancellationToken>())).ReturnsAsync((EntraAppRoleAssignment?)null);

            var result = await Service().RevokeAccessAsync(row.ManagedUserId);

            Assert.Equal(AccessStatus.Revoked, result.AccessStatus);
            Assert.False(result.HasAssignment);
            directory.Verify(d => d.DeleteUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Resend_refuses_once_the_invitation_is_accepted()
        {
            var row = await SeedAsync("oid-1", "pat@example.com", EntraUserType.Guest);
            directory.Setup(d => d.GetUserAsync("oid-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraUser("oid-1", "Pat", "pat@example.com", null, EntraUserType.Guest, true, InvitationStatus.Accepted));

            await Assert.ThrowsAsync<ConflictException>(() => Service().ResendInviteAsync(row.ManagedUserId));

            Assert.Equal(InvitationStatus.Accepted, (await host.Context.ManagedUsers.SingleAsync()).InvitationStatus);
            email.Verify(e => e.SendAsync(It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task List_syncs_stale_rows_with_one_listing_and_a_batch()
        {
            await SeedAsync("oid-1", "pat@example.com", EntraUserType.Guest);
            await SeedAsync("oid-2", "sam@example.com", EntraUserType.Guest);

            directory.Setup(d => d.ListAssignmentsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([new EntraAppRoleAssignment("assignment-1", "oid-1", Guid.Empty.ToString())]);
            directory.Setup(d => d.GetUsersAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, EntraUser>
                {
                    ["oid-1"] = new("oid-1", "Pat", "pat@example.com", null, EntraUserType.Guest, true, InvitationStatus.Accepted),
                    ["oid-2"] = new("oid-2", "Sam", "sam@example.com", null, EntraUserType.Guest, false, InvitationStatus.PendingAcceptance),
                });

            var users = await Service().ListAsync();

            Assert.Equal(InvitationStatus.Accepted, users.Single(u => u.Email == "pat@example.com").InvitationStatus);
            var sam = users.Single(u => u.Email == "sam@example.com");
            Assert.Equal(AccessStatus.Revoked, sam.AccessStatus);
            Assert.False(sam.AccountEnabled);
            directory.Verify(d => d.GetUsersAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
            directory.Verify(d => d.GetUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Onboarding_dry_run_reports_without_calling_graph()
        {
            host.Context.Users.AddRange(
                TestHost.User(10, "swa-10", "Alex@Example.com"),
                TestHost.User(11, "swa-11", "octocat", identityProvider: "github"));
            await host.Context.SaveChangesAsync();

            var report = await Service().OnboardExistingUsersAsync(dryRun: true, sendInvitationEmails: true);

            Assert.Equal("WouldInvite", report.Single(r => r.UserId == 10).Outcome);
            Assert.Equal("alex@example.com", report.Single(r => r.UserId == 10).Email);
            Assert.Equal("Skipped", report.Single(r => r.UserId == 11).Outcome);
            directory.VerifyNoOtherCalls();
        }

        private async Task<ManagedUser> SeedAsync(string objectId, string emailAddress, string userType, int? userId = null)
        {
            if (userId != null && !await host.Context.Users.AnyAsync(u => u.UserId == userId))
            {
                host.Context.Users.Add(TestHost.User(userId.Value, $"swa-{userId}", emailAddress));
            }

            var row = new ManagedUser
            {
                EntraObjectId = objectId,
                Email = emailAddress,
                DisplayName = emailAddress,
                UserType = userType,
                InvitationStatus = userType == EntraUserType.Member ? InvitationStatus.NotApplicable : InvitationStatus.PendingAcceptance,
                AccessStatus = AccessStatus.Active,
                AppRoleAssignmentId = "assignment-1",
                UserId = userId
            };
            host.Context.ManagedUsers.Add(row);
            await host.Context.SaveChangesAsync();
            return row;
        }

        public void Dispose() => host.Dispose();
    }
}
