using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.UserManagement.Commands
{
    /// <summary>
    /// Hard delete: revokes access, deletes the guest account from the Entra tenant and removes the local record.
    /// Refused for members of the tenant.
    /// </summary>
    [Authorize(Policy = PolicyNames.UserWrite)]
    [Authorize(Policy = PolicyNames.UserDelete)]
    public record DeleteManagedUserCommand(int Id) : IRequest;

    public class DeleteManagedUserCommandHandler(UserManagementService service) : IRequestHandler<DeleteManagedUserCommand>
    {
        public Task Handle(DeleteManagedUserCommand request, CancellationToken cancellationToken) =>
            service.DeleteAsync(request.Id, cancellationToken);
    }
}
