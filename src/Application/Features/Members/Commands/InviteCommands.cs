using Elysian.Application.Exceptions;
using Elysian.Application.Features.UserManagement;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Elysian.Application.Features.Members.Commands
{
    /// <summary>
    /// Makes a one-time code that gives whoever redeems it a role in this tenant
    /// </summary>
    [Authorize(Policy = PolicyNames.MemberWrite)]
    public record CreateInviteCommand(string? Role, string? Email = null) : IRequest<CreatedInvite>;

    public class CreateInviteCommandValidator : AbstractValidator<CreateInviteCommand>
    {
        public CreateInviteCommandValidator()
        {
            RuleFor(v => v.Role)
                .Must(r => MemberRoles.Normalize(r) != null)
                .WithMessage("Choose owner or editor.");

            RuleFor(v => v.Email)
                .MaximumLength(320).WithMessage("Email must be 320 characters or fewer.")
                .Must(e => UserManagementService.LooksLikeEmail(e)).WithMessage("Please enter a valid email address.")
                .When(v => !string.IsNullOrWhiteSpace(v.Email));
        }
    }

    public class CreateInviteCommandHandler(ElysianContext context, TimeProvider timeProvider, IOptions<AuthSettings> options)
        : IRequestHandler<CreateInviteCommand, CreatedInvite>
    {
        public async Task<CreatedInvite> Handle(CreateInviteCommand request, CancellationToken cancellationToken)
        {
            var code = InviteCodes.Generate();
            var invite = new TenantInvite
            {
                CodeHash = InviteCodes.Hash(code),
                Role = MemberRoles.Normalize(request.Role)!,
                Email = string.IsNullOrWhiteSpace(request.Email) ? null : UserManagementService.NormalizeEmail(request.Email),
                ExpiresAt = timeProvider.GetUtcNow().AddDays(Math.Max(1, options.Value.InviteLifetimeDays)),
            };
            context.TenantInvites.Add(invite);
            await context.SaveChangesAsync(cancellationToken);

            return new CreatedInvite(invite.TenantInviteId, code, invite.Role, invite.ExpiresAt);
        }
    }

    [Authorize(Policy = PolicyNames.MemberWrite)]
    public record RevokeInviteCommand(int Id) : IRequest;

    public class RevokeInviteCommandHandler(ElysianContext context, TimeProvider timeProvider) : IRequestHandler<RevokeInviteCommand>
    {
        public async Task Handle(RevokeInviteCommand request, CancellationToken cancellationToken)
        {
            var invite = await context.TenantInvites.FirstOrDefaultAsync(i => i.TenantInviteId == request.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(TenantInvite), request.Id);

            if (invite.RedeemedAt != null)
            {
                throw new ConflictException("This invite has already been used. Remove the member instead.");
            }

            invite.RevokedAt ??= timeProvider.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Makes the signed-in person a member of the tenant the code was made for. The tenant isn't known until the
    /// code is looked up, so this runs outside any tenant, through <see cref="PlatformContext"/>.
    /// </summary>
    [RequiresSignIn]
    public record RedeemInviteCommand(string? Code) : IRequest<RedeemedInvite>
    {
        // Keeps the code out of request logging
        public override string ToString() => nameof(RedeemInviteCommand);
    }

    public class RedeemInviteCommandHandler(PlatformContext platform, IClaimsPrincipalAccessor claimsPrincipalAccessor,
        IMultiTenantStore<ElysianTenantInfo> tenantStore, TimeProvider timeProvider) : IRequestHandler<RedeemInviteCommand, RedeemedInvite>
    {
        public async Task<RedeemedInvite> Handle(RedeemInviteCommand request, CancellationToken cancellationToken)
        {
            var principal = claimsPrincipalAccessor.Principal;
            var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new UnauthorizedAccessException();
            }

            var emailClaim = principal!.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;
            var email = UserManagementService.LooksLikeEmail(emailClaim) ? UserManagementService.NormalizeEmail(emailClaim!) : null;
            var now = timeProvider.GetUtcNow();

            var hash = InviteCodes.Hash(request.Code);
            var invite = await platform.TenantInvites.FirstOrDefaultAsync(i => i.CodeHash == hash, cancellationToken);

            // One answer for every way a code can be wrong, so a guess learns nothing
            if (invite == null || !invite.CanBeRedeemedAt(now) || (invite.Email != null && invite.Email != email))
            {
                throw Invalid();
            }

            var tenant = await tenantStore.TryGetAsync(invite.TenantId) ?? throw Invalid();

            var membership = await platform.TenantMemberships.FirstOrDefaultAsync(
                m => m.TenantId == invite.TenantId && (m.Subject == subject || (m.Subject == null && email != null && m.Email == email)),
                cancellationToken);

            if (membership == null)
            {
                membership = new TenantMembership
                {
                    TenantId = invite.TenantId,
                    Email = email,
                    CreatedByUserId = subject,
                    CreatedAt = now,
                };
                platform.TenantMemberships.Add(membership);
            }

            membership.Subject = subject;
            membership.Role = invite.Role;
            membership.ModifiedByUserId = subject;
            membership.ModifiedAt = now;

            invite.RedeemedAt = now;
            invite.RedeemedBySubject = subject;
            invite.ModifiedByUserId = subject;
            invite.ModifiedAt = now;

            await platform.SaveChangesAsync(cancellationToken);

            return new RedeemedInvite(tenant.Identifier!, invite.Role);
        }

        private static CustomValidationException Invalid() =>
            new([new ValidationFailure(nameof(RedeemInviteCommand.Code), MemberRoles.InvalidInviteMessage)]);
    }
}
