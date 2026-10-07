using Elysian.Application.Features.UserManagement.Models;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.UserManagement.Queries
{
    /// <summary>
    /// Managed users with their cached Entra state. <see cref="Refresh"/> syncs with Entra first.
    /// </summary>
    [Authorize(Policy = PolicyNames.UserWrite)]
    public record GetManagedUsersQuery(bool Refresh = false) : IRequest<List<ManagedUserModel>>;

    public class GetManagedUsersQueryHandler(UserManagementService service) : IRequestHandler<GetManagedUsersQuery, List<ManagedUserModel>>
    {
        public Task<List<ManagedUserModel>> Handle(GetManagedUsersQuery request, CancellationToken cancellationToken) =>
            service.ListAsync(request.Refresh, cancellationToken);
    }
}
