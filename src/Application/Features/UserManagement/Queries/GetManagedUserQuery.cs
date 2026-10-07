using Elysian.Application.Features.UserManagement.Models;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.UserManagement.Queries
{
    /// <summary>
    /// One managed user, refreshed from Entra
    /// </summary>
    [Authorize(Policy = PolicyNames.UserWrite)]
    public record GetManagedUserQuery(int Id) : IRequest<ManagedUserModel>;

    public class GetManagedUserQueryHandler(UserManagementService service) : IRequestHandler<GetManagedUserQuery, ManagedUserModel>
    {
        public Task<ManagedUserModel> Handle(GetManagedUserQuery request, CancellationToken cancellationToken) =>
            service.GetAsync(request.Id, cancellationToken);
    }
}
