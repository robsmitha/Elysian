using System.Security.Claims;
using Elysian.Application.Behaviors;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Members.Commands;
using Elysian.Application.Features.Members.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Identity;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.Platforms
{
    public sealed class MembershipTests : IDisposable
    {
        private const string AdminSubject = "platform-admin-oid";
        private static readonly ElysianTenantInfo OtherTenant = new() { Id = "tenant-2", Identifier = "other", Name = "Other", Platform = PlatformNames.Photography, Runtime = RuntimeNames.Cloud };

        private readonly TestHost host = new();
        private readonly ServiceProvider tenants;
        private readonly PlatformContext platform;
        private readonly AuthSettings settings = new() { RoleSource = RoleSource.Membership, PlatformAdminSubjects = [AdminSubject] };
        private readonly Clock clock = new();

        public MembershipTests()
        {
            host.Context.Merchants.Add(new Merchant { MerchantIdentifier = TestHost.Tenant.Identifier!, Name = "Test", Description = "Test", WebsiteUrl = "https://example.com" });
            host.Context.SaveChanges();

            var services = new ServiceCollection();
            services.AddMultiTenant<ElysianTenantInfo>().WithInMemoryStore(options =>
            {
                options.Tenants.Add(TestHost.Tenant);
                options.Tenants.Add(OtherTenant);
            });
            tenants = services.BuildServiceProvider();
            platform = new PlatformContext(new DbContextOptionsBuilder<PlatformContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        }

        public void Dispose()
        {
            platform.Dispose();
            tenants.Dispose();
            host.Dispose();
        }

        // ---- Roles from membership

        [Fact]
        public async Task Someone_with_no_membership_is_refused()
        {
            await Assert.ThrowsAsync<ForbiddenAccessException>(() => Identity().GetOrCreateAsync(Person("stranger", "stranger@example.com")));

            Assert.Empty(host.Context.Users);
        }

        [Fact]
        public async Task A_member_gets_the_policies_of_their_role_whatever_the_token_says()
        {
            await AddMembershipAsync("owner-1", "owner@example.com", RoleNames.Owner);
            await AddMembershipAsync("editor-1", "editor@example.com", RoleNames.Editor);
            var identity = Identity();

            var owner = await identity.GetOrCreateAsync(Person("owner-1", "owner@example.com"));
            var editor = await identity.GetOrCreateAsync(Person("editor-1", "editor@example.com", tokenRoles: RoleNames.SystemAdmin));

            Assert.True(await identity.AuthorizeAsync(owner, PolicyNames.MemberWrite));
            Assert.True(await identity.AuthorizeAsync(owner, PolicyNames.PhotoWrite));
            Assert.False(await identity.AuthorizeAsync(owner, PolicyNames.UserWrite));

            Assert.True(await identity.AuthorizeAsync(editor, PolicyNames.PhotoWrite));
            Assert.False(await identity.AuthorizeAsync(editor, PolicyNames.MemberWrite));
            Assert.False(await identity.IsInRoleAsync(editor, RoleNames.SystemAdmin));
        }

        [Fact]
        public async Task A_changed_role_takes_effect_on_the_next_request()
        {
            var membership = await AddMembershipAsync("person-1", "person@example.com", RoleNames.Owner);
            var identity = Identity();
            await identity.GetOrCreateAsync(Person("person-1", "person@example.com"));

            membership.Role = RoleNames.Editor;
            await host.Context.SaveChangesAsync();
            var user = await identity.GetOrCreateAsync(Person("person-1", "person@example.com"));

            Assert.False(await identity.AuthorizeAsync(user, PolicyNames.MemberWrite));
            Assert.Single(host.Context.Users);
        }

        [Fact]
        public async Task A_removed_member_is_refused()
        {
            var membership = await AddMembershipAsync("person-1", "person@example.com", RoleNames.Editor);
            var identity = Identity();
            await identity.GetOrCreateAsync(Person("person-1", "person@example.com"));

            membership.IsDeleted = true;
            await host.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<ForbiddenAccessException>(() => identity.GetOrCreateAsync(Person("person-1", "person@example.com")));
        }

        [Fact]
        public async Task A_membership_made_by_email_is_claimed_on_first_sign_in()
        {
            var membership = await AddMembershipAsync(null, "owner@example.com", RoleNames.Owner);

            await Identity().GetOrCreateAsync(Person("new-oid", "Owner@Example.com"));

            Assert.Equal("new-oid", membership.Subject);
            await Assert.ThrowsAsync<ForbiddenAccessException>(() => Identity().GetOrCreateAsync(Person("someone-else", "other@example.com")));
        }

        [Fact]
        public async Task The_existing_user_and_their_data_follow_them_to_the_new_sign_in_id()
        {
            await AddMembershipAsync(null, "owner@example.com", RoleNames.Owner);
            host.Context.Users.Add(TestHost.User(0, "old-swa-id", "owner@example.com", policies: PolicyNames.BudgetRead));
            host.Context.BillTrackings.Add(new BillTracking { UserId = "old-swa-id", Congress = 119, BillType = "hr", BillNumber = 1, Title = "A bill" });
            await host.Context.SaveChangesAsync();

            var user = await Identity().GetOrCreateAsync(Person("new-oid", "owner@example.com"));

            Assert.Single(host.Context.Users);
            Assert.Equal("new-oid", user.ExternalUserId);
            Assert.Equal(SignInClaims.ExternalIdProvider, user.IdentityProvider);
            Assert.Equal("new-oid", host.Context.BillTrackings.Single().UserId);
        }

        [Fact]
        public async Task A_platform_admin_may_act_in_any_tenant_and_the_first_time_is_audited()
        {
            var identity = Identity();

            var user = await identity.GetOrCreateAsync(Person(AdminSubject, "rob@example.com"));
            await identity.GetOrCreateAsync(Person(AdminSubject, "rob@example.com"));

            Assert.True(await identity.IsInRoleAsync(user, RoleNames.SystemAdmin));
            Assert.True(await identity.AuthorizeAsync(user, PolicyNames.MemberWrite));
            var audit = Assert.Single(host.Context.UserAuditLogs);
            Assert.Equal(IdentityService.PlatformAdminAccessAction, audit.Action);
            Assert.Equal(AdminSubject, audit.ActorUserId);
        }

        [Fact]
        public async Task Hosts_that_take_roles_from_the_principal_are_unchanged()
        {
            var identity = new IdentityService(host.Context, host.TenantAccessor, TimeProvider.System, NullLogger<IdentityService>.Instance,
                Options.Create(new AuthSettings()));

            var user = await identity.GetOrCreateAsync(TestHost.Principal("swa-id", "admin@example.com", roles: RoleNames.MerchantAdmin));

            Assert.True(await identity.AuthorizeAsync(user, PolicyNames.PhotoWrite));
            Assert.False(await identity.AuthorizeAsync(user, PolicyNames.MemberWrite));
        }

        // ---- Invites

        [Fact]
        public async Task Redeeming_an_invite_makes_the_person_a_member_of_that_tenant_only()
        {
            var created = await CreateInviteAsync(RoleNames.Editor);
            Assert.Equal(InviteCodes.Hash(created.Code), host.Context.TenantInvites.Single().CodeHash);
            await CopyInvitesToPlatformAsync();

            var redeemed = await RedeemAsync(created.Code.ToLowerInvariant().Replace("-", " "), Person("new-oid", "friend@example.com"));

            Assert.Equal(TestHost.Tenant.Identifier, redeemed.TenantIdentifier);
            var membership = await platform.TenantMemberships.SingleAsync();
            Assert.Equal(TestHost.Tenant.Id, membership.TenantId);
            Assert.Equal("new-oid", membership.Subject);
            Assert.Equal("friend@example.com", membership.Email);
            Assert.Equal(RoleNames.Editor, membership.Role);
        }

        [Fact]
        public async Task An_invite_works_once()
        {
            var created = await CreateInviteAsync(RoleNames.Editor);
            await CopyInvitesToPlatformAsync();
            await RedeemAsync(created.Code, Person("first", "first@example.com"));

            await Assert.ThrowsAsync<CustomValidationException>(() => RedeemAsync(created.Code, Person("second", "second@example.com")));
            Assert.Single(platform.TenantMemberships);
        }

        [Fact]
        public async Task Expired_revoked_unknown_and_wrong_person_invites_are_all_refused_the_same_way()
        {
            var expired = await CreateInviteAsync(RoleNames.Editor);
            var revoked = await CreateInviteAsync(RoleNames.Editor);
            var forSomeoneElse = await CreateInviteAsync(RoleNames.Owner, "invited@example.com");
            await new RevokeInviteCommandHandler(host.Context, clock).Handle(new RevokeInviteCommand(revoked.Id), CancellationToken.None);
            host.Context.TenantInvites.Single(i => i.TenantInviteId == expired.Id).ExpiresAt = clock.GetUtcNow().AddMinutes(-1);
            await host.Context.SaveChangesAsync();
            await CopyInvitesToPlatformAsync();

            foreach (var code in new[] { expired.Code, revoked.Code, forSomeoneElse.Code, "AAAA-BBBB-CCCC-DDDD", "", null })
            {
                var exception = await Assert.ThrowsAsync<CustomValidationException>(() => RedeemAsync(code, Person("new-oid", "friend@example.com")));
                Assert.Contains(exception.Errors, e => e.Key == nameof(RedeemInviteCommand.Code));
            }

            Assert.Empty(platform.TenantMemberships);
            Assert.Equal(RoleNames.Owner, (await RedeemAsync(forSomeoneElse.Code, Person("invited-oid", "Invited@example.com"))).Role);
        }

        [Fact]
        public async Task Only_owner_and_editor_can_be_invited()
        {
            var validator = new CreateInviteCommandValidator();

            Assert.True(validator.Validate(new CreateInviteCommand("Owner")).IsValid);
            Assert.True(validator.Validate(new CreateInviteCommand(RoleNames.Editor, "a@example.com")).IsValid);
            Assert.False(validator.Validate(new CreateInviteCommand(RoleNames.SystemAdmin)).IsValid);
            Assert.False(validator.Validate(new CreateInviteCommand(RoleNames.MerchantAdmin)).IsValid);
            Assert.False(validator.Validate(new CreateInviteCommand(RoleNames.Editor, "not an email")).IsValid);
            await Task.CompletedTask;
        }

        // ---- Members

        [Fact]
        public async Task The_last_owner_cant_be_removed()
        {
            var owner = await AddMembershipAsync("owner-1", "owner@example.com", RoleNames.Owner);
            var editor = await AddMembershipAsync("editor-1", "editor@example.com", RoleNames.Editor);
            var handler = new RemoveMemberCommandHandler(host.Context);

            await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new RemoveMemberCommand(owner.TenantMembershipId), CancellationToken.None));
            await handler.Handle(new RemoveMemberCommand(editor.TenantMembershipId), CancellationToken.None);

            var remaining = await new GetMembersQueryHandler(host.Context).Handle(new GetMembersQuery(), CancellationToken.None);
            Assert.Equal("owner@example.com", Assert.Single(remaining).Email);
        }

        [Fact]
        public async Task Backfill_gives_existing_admins_an_owner_membership_once()
        {
            host.Context.Users.Add(User("swa-1", "admin@example.com", RoleNames.MerchantAdmin));
            host.Context.Users.Add(User("swa-2", "visitor@example.com", RoleNames.Authenticated));
            host.Context.Users.Add(User("swa-3", "github-handle", RoleNames.SystemAdmin));
            await host.Context.SaveChangesAsync();
            var handler = new BackfillMembershipsCommandHandler(host.Context);

            Assert.Equal(1, await handler.Handle(new BackfillMembershipsCommand(), CancellationToken.None));
            Assert.Equal(0, await handler.Handle(new BackfillMembershipsCommand(), CancellationToken.None));

            var membership = Assert.Single(host.Context.TenantMemberships);
            Assert.Equal("admin@example.com", membership.Email);
            Assert.Equal(RoleNames.Owner, membership.Role);
            Assert.Null(membership.Subject);
        }

        [Fact]
        public async Task My_sites_lists_only_where_the_person_is_a_member_and_everything_for_a_platform_admin()
        {
            platform.TenantMemberships.Add(Row(OtherTenant.Id!, "person-1", "person@example.com", RoleNames.Editor));
            platform.TenantMemberships.Add(Row(TestHost.Tenant.Id!, "someone-else", "else@example.com", RoleNames.Owner));
            await platform.SaveChangesAsync();

            var mine = await MyTenantsAsync(Person("person-1", "person@example.com"));
            var admins = await MyTenantsAsync(Person(AdminSubject, "rob@example.com"));
            var strangers = await MyTenantsAsync(Person("stranger", "stranger@example.com"));

            var site = Assert.Single(mine);
            Assert.Equal("other", site.Identifier);
            Assert.Equal(RoleNames.Editor, site.Role);
            Assert.Equal(2, admins.Count);
            Assert.Empty(strangers);
        }

        // ---- Pipeline

        [Fact]
        public async Task Requests_that_need_a_sign_in_refuse_anonymous_callers_without_needing_a_tenant()
        {
            var anonymous = new ClaimsPrincipalAccessor { Principal = new ClaimsPrincipal() };
            var signedIn = new ClaimsPrincipalAccessor { Principal = Person("person-1", "person@example.com") };
            var handled = false;
            Task<List<Elysian.Application.Features.Members.MyTenantModel>> Next() { handled = true; return Task.FromResult(new List<Elysian.Application.Features.Members.MyTenantModel>()); }

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                new AuthorizationBehavior<GetMyTenantsQuery, List<Elysian.Application.Features.Members.MyTenantModel>>(anonymous, null!)
                    .Handle(new GetMyTenantsQuery(), Next, CancellationToken.None));
            Assert.False(handled);

            await new AuthorizationBehavior<GetMyTenantsQuery, List<Elysian.Application.Features.Members.MyTenantModel>>(signedIn, null!)
                .Handle(new GetMyTenantsQuery(), Next, CancellationToken.None);
            Assert.True(handled);
        }

        private IdentityService Identity() =>
            new(host.Context, host.TenantAccessor, clock, NullLogger<IdentityService>.Instance, Options.Create(settings));

        private static ClaimsPrincipal Person(string subject, string email, string? tokenRoles = null)
        {
            var identity = new ClaimsIdentity(SignInClaims.ExternalIdProvider);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
            identity.AddClaim(new Claim(ClaimTypes.Name, email));
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
            identity.AddClaim(new Claim(SignInClaims.IdentityProvider, SignInClaims.ExternalIdProvider));
            if (tokenRoles != null)
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, tokenRoles));
            }
            return new ClaimsPrincipal(identity);
        }

        private static User User(string externalUserId, string userName, string role) => new()
        {
            ExternalUserId = externalUserId,
            UserName = userName,
            IdentityProvider = "aad",
            AccessControl = new AccessControl { Roles = [role], Policies = PolicyNames.GetDefaultPolicies([role]) }
        };

        private async Task<TenantMembership> AddMembershipAsync(string? subject, string email, string role)
        {
            var membership = new TenantMembership { Subject = subject, Email = email, Role = role };
            host.Context.TenantMemberships.Add(membership);
            await host.Context.SaveChangesAsync();
            return membership;
        }

        private static TenantMembership Row(string tenantId, string subject, string email, string role) => new()
        {
            TenantId = tenantId, Subject = subject, Email = email, Role = role, CreatedByUserId = "test", ModifiedByUserId = "test"
        };

        private Task<Elysian.Application.Features.Members.CreatedInvite> CreateInviteAsync(string role, string? email = null) =>
            new CreateInviteCommandHandler(host.Context, clock, Options.Create(settings)).Handle(new CreateInviteCommand(role, email), CancellationToken.None);

        private Task<Elysian.Application.Features.Members.RedeemedInvite> RedeemAsync(string? code, ClaimsPrincipal person) =>
            new RedeemInviteCommandHandler(platform, new ClaimsPrincipalAccessor { Principal = person },
                tenants.GetRequiredService<IMultiTenantStore<ElysianTenantInfo>>(), clock).Handle(new RedeemInviteCommand(code), CancellationToken.None);

        private Task<List<Elysian.Application.Features.Members.MyTenantModel>> MyTenantsAsync(ClaimsPrincipal person) =>
            new GetMyTenantsQueryHandler(platform, new ClaimsPrincipalAccessor { Principal = person },
                tenants.GetRequiredService<IMultiTenantStore<ElysianTenantInfo>>(), Options.Create(settings)).Handle(new GetMyTenantsQuery(), CancellationToken.None);

        /// <summary>
        /// Invites are made through ElysianContext and redeemed through PlatformContext; in production both are
        /// the same table, here they're separate in-memory databases.
        /// </summary>
        private async Task CopyInvitesToPlatformAsync()
        {
            foreach (var invite in await host.Context.TenantInvites.AsNoTracking().ToListAsync())
            {
                invite.TenantInviteId = 0;
                platform.TenantInvites.Add(invite);
            }
            await platform.SaveChangesAsync();
        }

        private sealed class Clock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        }
    }
}
