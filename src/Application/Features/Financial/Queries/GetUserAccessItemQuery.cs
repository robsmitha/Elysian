using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetUserAccessItemQuery(int UserAccessItemId) : IRequest<InstitutionAccessItemModel>;

    public class GetUserAccessItemQueryHandler(IConnectionService connectionService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<GetUserAccessItemQuery, InstitutionAccessItemModel>
    {
        public async Task<InstitutionAccessItemModel> Handle(GetUserAccessItemQuery request, CancellationToken cancellationToken)
        {
            // Only finds banks linked by the signed-in person
            var items = await connectionService.GetAccessItemsAsync(claimsPrincipalAccessor.UserId!, request.UserAccessItemId);
            return items.SingleOrDefault() ?? throw new NotFoundException("Linked bank", request.UserAccessItemId);
        }
    }
}
