using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetUserAccessItemsQuery : IRequest<List<InstitutionAccessItemModel>>;

    public class GetUserAccessItemsQueryHandler(IFinancialService financialService, IAccessTokenService accessTokenService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
            : IRequestHandler<GetUserAccessItemsQuery, List<InstitutionAccessItemModel>>
    {
        public async Task<List<InstitutionAccessItemModel>> Handle(GetUserAccessItemsQuery request, CancellationToken cancellationToken)
        {
            var accessTokens = await accessTokenService.GetAccessTokensAsync(claimsPrincipalAccessor.UserId);
            var userAccessItems = new List<InstitutionAccessItemModel>();
            foreach (var accessToken in accessTokens)
            {
                // Every bank comes back, with a status when Plaid can't read it
                userAccessItems.Add(await AccessItemLoader.LoadAsync(financialService, accessToken));
            }
            return userAccessItems;
        }
    }
}
