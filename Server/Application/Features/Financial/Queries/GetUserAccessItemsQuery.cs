using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    public class GetUserAccessItemsQuery : IRequest<List<InstitutionAccessItemModel>>
    {
        public class Handler(IFinancialService financialService, IAccessTokenService accessTokenService, IClaimsPrincipalAccessor claimsPrincipalAccessor) 
            : IRequestHandler<GetUserAccessItemsQuery, List<InstitutionAccessItemModel>>
        {
            public async Task<List<InstitutionAccessItemModel>> Handle(GetUserAccessItemsQuery request, CancellationToken cancellationToken)
            {
                var accessTokens = await accessTokenService.GetAccessTokensAsync(claimsPrincipalAccessor.UserId);
                var userAccessItems = new List<InstitutionAccessItemModel>();
                foreach(var accessToken in accessTokens)
                {
                    var institution = await financialService.GetInstitutionAsync(accessToken.InstitutionId);
                    try
                    {
                        var accounts = await financialService.GetAccountsAsync(accessToken.AccessToken);
                        var item = await financialService.GetItemAsync(accessToken.AccessToken);
                        userAccessItems.Add(new InstitutionAccessItemModel
                        {
                            Accounts = accounts,
                            Institution = institution,
                            Item = item,
                            InstitutionAccessItemId = accessToken.InstitutionAccessItemId
                        });
                    }
                    catch (FinancialServiceException fex)
                    {
                        var errorAccessItem = new InstitutionAccessItemModel
                        {
                            Institution = institution,
                            InstitutionAccessItemId = accessToken.InstitutionAccessItemId
                        };

                        if (string.Equals(fex.Error?.error_code, PlaidErrorCodes.ITEM_LOGIN_REQUIRED, StringComparison.InvariantCultureIgnoreCase))
                        {
                            errorAccessItem.ExpiredAccessItem = new ExpiredAccessItem(accessToken.AccessToken, fex.Error.error_message, institution);
                        }

                        userAccessItems.Add(errorAccessItem);

                        continue;
                    }
                }
                return userAccessItems;
            }
        }
    }
}
