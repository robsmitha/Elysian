using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    /// <summary>The person's banks with accounts, balances and status, as stored by the last sync.</summary>
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetUserAccessItemsQuery : IRequest<List<InstitutionAccessItemModel>>;

    public class GetUserAccessItemsQueryHandler(IConnectionService connectionService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
            : IRequestHandler<GetUserAccessItemsQuery, List<InstitutionAccessItemModel>>
    {
        public async Task<List<InstitutionAccessItemModel>> Handle(GetUserAccessItemsQuery request, CancellationToken cancellationToken) =>
            await connectionService.GetAccessItemsAsync(claimsPrincipalAccessor.UserId!);
    }
}
