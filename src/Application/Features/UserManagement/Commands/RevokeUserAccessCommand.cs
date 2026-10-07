using Elysian.Application.Features.UserManagement.Models;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.UserManagement.Commands
{
    /// <summary>
    /// Removes the user's assignment to the enterprise app. Their Entra account is kept.
    /// </summary>
    [Authorize(Policy = PolicyNames.UserWrite)]
    public record RevokeUserAccessCommand(int Id) : IRequest<ManagedUserModel>;

    public class RevokeUserAccessCommandHandler(UserManagementService service) : IRequestHandler<RevokeUserAccessCommand, ManagedUserModel>
    {
        public Task<ManagedUserModel> Handle(RevokeUserAccessCommand request, CancellationToken cancellationToken) =>
            service.RevokeAccessAsync(request.Id, cancellationToken);
    }
}
