﻿using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize]
    public record CreateLinkTokenCommand(string AccessToken) : IRequest<LinkTokenModel>;

    public class CreateLinkTokenCommandHandler(IFinancialService financialService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<CreateLinkTokenCommand, LinkTokenModel>
    {
        public async Task<LinkTokenModel> Handle(CreateLinkTokenCommand request, CancellationToken cancellationToken)
        {
            return await financialService.CreateLinkTokenAsync(claimsPrincipalAccessor.UserId, request.AccessToken);
        }
    }
}
