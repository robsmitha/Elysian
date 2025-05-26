using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Merchants.Queries
{
    [Authorize(Policy = PolicyNames.UserRead)]
    public record GetUsersQuery : IRequest<List<User>>;

    public class GetUsersQueryHandler(ElysianContext context) : IRequestHandler<GetUsersQuery, List<User>>
    {
        public async Task<List<User>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
        {
            return await context.Users.AsNoTracking().ToListAsync(cancellationToken);
        }
    }
}
