using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Congress.Commands
{
    /// <summary>
    /// Removes a saved bill. Removing one that isn't saved does nothing.
    /// </summary>
    /// <remarks>
    /// A hard delete: a saved bill has no history worth keeping, and bringing back a soft deleted row would
    /// need IgnoreQueryFilters, which also drops the tenant filter.
    /// </remarks>
    [Authorize]
    public record UntrackBillCommand(int Congress, string BillType, int BillNumber) : IRequest;

    public class UntrackBillCommandHandler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<UntrackBillCommand>
    {
        public async Task Handle(UntrackBillCommand request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId!;
            var billType = request.BillType?.ToUpperInvariant();

            await context.BillTrackings
                .Where(b => b.UserId == userId
                    && b.Congress == request.Congress
                    && b.BillType == billType
                    && b.BillNumber == request.BillNumber)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
