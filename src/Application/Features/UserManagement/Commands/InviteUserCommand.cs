using Elysian.Application.Features.UserManagement.Models;
using Elysian.Domain.Security;
using FluentValidation;
using MediatR;

namespace Elysian.Application.Features.UserManagement.Commands
{
    /// <summary>
    /// Invites someone as a B2B guest (or assigns an existing member), gives them access to the enterprise app and
    /// emails them the invitation. Inviting an email that's already managed re-invites rather than duplicating.
    /// </summary>
    [Authorize(Policy = PolicyNames.UserWrite)]
    public record InviteUserCommand(string? Email, string? DisplayName) : IRequest<InviteUserResult>
    {
        // Keeps the invitee's email out of request logging
        public override string ToString() => nameof(InviteUserCommand);
    }

    public class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
    {
        public InviteUserCommandValidator()
        {
            RuleFor(v => v.Email)
                .NotEmpty().WithMessage("Please enter an email address.")
                .MaximumLength(320).WithMessage("Email must be 320 characters or fewer.")
                .EmailAddress().WithMessage("Please enter a valid email address.")
                .Must(e => UserManagementService.LooksLikeEmail(e)).WithMessage("Please enter a valid email address.");

            RuleFor(v => v.DisplayName)
                .NotEmpty().WithMessage("Please enter a display name.")
                .MaximumLength(256).WithMessage("Display name must be 256 characters or fewer.");
        }
    }

    public class InviteUserCommandHandler(UserManagementService service) : IRequestHandler<InviteUserCommand, InviteUserResult>
    {
        public Task<InviteUserResult> Handle(InviteUserCommand request, CancellationToken cancellationToken) =>
            service.InviteAsync(request.Email!, request.DisplayName!, sendInvitationEmail: true, cancellationToken);
    }
}
