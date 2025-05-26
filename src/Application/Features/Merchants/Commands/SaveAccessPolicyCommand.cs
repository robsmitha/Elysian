using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Merchants.Commands
{
    [Authorize(Policy = PolicyNames.UserWrite)]
    public record SaveAccessPolicyCommand(int UserId, AccessControl AccessControl) : IRequest<AccessControl>;

    public class SaveAccessPolicyCommandHandler(ElysianContext context)
        : IRequestHandler<SaveAccessPolicyCommand, AccessControl>
    {
        public async Task<AccessControl> Handle(SaveAccessPolicyCommand request, CancellationToken cancellationToken)
        {
            var user = await context.Users.FindAsync([request.UserId], cancellationToken: cancellationToken)
                ?? throw new NotFoundException(request.UserId.ToString());

            var incomingPolicies = request.AccessControl.Policies ?? [];

            user.AccessControl.Policies.RemoveAll(item => !incomingPolicies.Contains(item));

            foreach (var item in incomingPolicies)
            {
                if (!user.AccessControl.Policies.Contains(item))
                {
                    user.AccessControl.Policies.Add(item);
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            return user.AccessControl;
        }
    }
}
