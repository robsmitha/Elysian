using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize]
    public class SetAccessTokenCommand(string publicToken) : IRequest<InstitutionAccessTokenModel>
    {
        public string PublicToken { get; set; } = publicToken;

        public class Handler(IFinancialService financialService, ILogger<SetAccessTokenCommand> logger,
            IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SetAccessTokenCommand, InstitutionAccessTokenModel>
        {
            public async Task<InstitutionAccessTokenModel> Handle(SetAccessTokenCommand request, CancellationToken cancellationToken)
            {
                var publicTokenExchangeResponse = await financialService.ItemPublicTokenExchangeAsync(request.PublicToken);
                
                return await financialService.SetAccessTokenAsync(claimsPrincipalAccessor.UserId, publicTokenExchangeResponse.access_token);
            }
        }
    }
}
