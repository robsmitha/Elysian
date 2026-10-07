using Elysian.Application.Features.UserManagement.Models;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.UserManagement.Commands
{
    /// <summary>
    /// Assigns the user to the enterprise app again
    /// </summary>
    [Authorize(Policy = PolicyNames.UserWrite)]
    public record RestoreUserAccessCommand(int Id) : IRequest<ManagedUserModel>;

    public class RestoreUserAccessCommandHandler(UserManagementService service) : IRequestHandler<RestoreUserAccessCommand, ManagedUserModel>
    {
        public Task<ManagedUserModel> Handle(RestoreUserAccessCommand request, CancellationToken cancellationToken) =>
            service.RestoreAccessAsync(request.Id, cancellationToken);
    }
}
