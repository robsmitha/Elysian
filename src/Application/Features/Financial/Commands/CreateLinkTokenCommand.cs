using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>
    /// A Plaid Link token. Without an id it links a new bank; with one it opens update mode so the person can sign in
    /// to that bank again. The access token is looked up here and never comes from, or goes to, the browser.
    /// </summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record CreateLinkTokenCommand(int? InstitutionAccessItemId = null, bool AccountSelection = false) : IRequest<LinkTokenModel>;

    public class CreateLinkTokenCommandHandler(IFinancialService financialService, IAccessTokenService accessTokenService,
        IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<CreateLinkTokenCommand, LinkTokenModel>
    {
        public async Task<LinkTokenModel> Handle(CreateLinkTokenCommand request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId;
            if (request.InstitutionAccessItemId is not int itemId)
            {
                return await financialService.CreateLinkTokenAsync(userId);
            }

            // Only finds banks linked by the signed-in person
            var accessToken = await accessTokenService.GetAccessTokenAsync(userId, itemId)
                ?? throw new NotFoundException("Linked bank", itemId);
            if (accessToken.AccessToken == null)
            {
                throw new NotFoundException("Linked bank", itemId);
            }
            return await financialService.CreateLinkTokenAsync(userId, accessToken.AccessToken, request.AccountSelection);
        }
    }
}
