using Elysian.Application.Features.UserManagement.Models;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.UserManagement.Commands
{
    /// <summary>
    /// Sends a fresh invitation email to a guest who hasn't accepted yet
    /// </summary>
    [Authorize(Policy = PolicyNames.UserWrite)]
    public record ResendInviteCommand(int Id) : IRequest<ManagedUserModel>;

    public class ResendInviteCommandHandler(UserManagementService service) : IRequestHandler<ResendInviteCommand, ManagedUserModel>
    {
        public Task<ManagedUserModel> Handle(ResendInviteCommand request, CancellationToken cancellationToken) =>
            service.ResendInviteAsync(request.Id, cancellationToken);
    }
}
