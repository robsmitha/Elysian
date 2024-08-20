using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Financial.Commands
{
    public class CreateLinkTokenCommand(string accessToken) : IRequest<LinkTokenModel>
    {
        public string? AccessToken { get; set; } = accessToken;

        public class Handler(ILogger<CreateLinkTokenCommand> logger, IFinancialService financialService, IClaimsPrincipalAccessor claimsPrincipalAccessor) 
            : IRequestHandler<CreateLinkTokenCommand, LinkTokenModel>
        {
            public async Task<LinkTokenModel> Handle(CreateLinkTokenCommand request, CancellationToken cancellationToken)
            {
                return await financialService.CreateLinkTokenAsync(claimsPrincipalAccessor.UserId, request.AccessToken);
            }
        }
    }
}
