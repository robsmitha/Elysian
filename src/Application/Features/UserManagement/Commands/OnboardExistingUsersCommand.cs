using Elysian.Application.Features.UserManagement.Models;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.UserManagement.Commands
{
    /// <summary>
    /// One-time step before sign-in starts requiring assignment: invites (or assigns) everyone who has signed in with
    /// Microsoft so they aren't locked out. Defaults to a dry run that only reports what it would do.
    /// </summary>
    [Authorize(Policy = PolicyNames.UserWrite)]
    public record OnboardExistingUsersCommand(bool DryRun = true, bool SendInvitationEmails = true) : IRequest<List<OnboardingReportItem>>;

    public class OnboardExistingUsersCommandHandler(UserManagementService service)
        : IRequestHandler<OnboardExistingUsersCommand, List<OnboardingReportItem>>
    {
        public Task<List<OnboardingReportItem>> Handle(OnboardExistingUsersCommand request, CancellationToken cancellationToken) =>
            service.OnboardExistingUsersAsync(request.DryRun, request.SendInvitationEmails, cancellationToken);
    }
}
