using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    public class GetUserAccessItemQuery(int userAccessItemId) : IRequest<InstitutionAccessItemModel>
    {
        public int UserAccessItemId { get; set; } = userAccessItemId;

        public class Handler(IFinancialService financialService, IAccessTokenService accessTokenService, IClaimsPrincipalAccessor claimsPrincipalAccessor) 
            : IRequestHandler<GetUserAccessItemQuery, InstitutionAccessItemModel>
        {
            public async Task<InstitutionAccessItemModel> Handle(GetUserAccessItemQuery request, CancellationToken cancellationToken)
            {
                var accessToken = await accessTokenService.GetAccessTokenAsync(claimsPrincipalAccessor.UserId, request.UserAccessItemId);
                var institution = await financialService.GetInstitutionAsync(accessToken.InstitutionId);
                try
                {
                    var accounts = await financialService.GetAccountsAsync(accessToken.AccessToken);
                    var item = await financialService.GetItemAsync(accessToken.AccessToken);
                    return new InstitutionAccessItemModel
                    {
                        Accounts = accounts,
                        Institution = institution,
                        Item = item,
                        InstitutionAccessItemId = accessToken.InstitutionAccessItemId
                    };
                }
                catch (FinancialServiceException fex)
                {
                    if (string.Equals(fex.Error?.error_code, PlaidErrorCodes.ITEM_LOGIN_REQUIRED,
                        StringComparison.InvariantCultureIgnoreCase))
                    {
                        return new InstitutionAccessItemModel
                        {
                            Institution = institution,
                            ExpiredAccessItem = new ExpiredAccessItem(accessToken.AccessToken, fex.Error.error_message, institution),
                            InstitutionAccessItemId = accessToken.InstitutionAccessItemId
                        };
                    }
                }
                return new InstitutionAccessItemModel
                {
                    Institution = institution,
                    InstitutionAccessItemId = accessToken.InstitutionAccessItemId
                };
            }
        }
    }
}
