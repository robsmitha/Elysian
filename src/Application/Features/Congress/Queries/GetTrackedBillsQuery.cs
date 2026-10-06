using Elysian.Application.Features.Congress.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Congress.Queries
{
    /// <summary>
    /// The signed in user's saved bills, most recently saved first
    /// </summary>
    [Authorize]
    public record GetTrackedBillsQuery : IRequest<List<TrackedBillModel>>;

    public class GetTrackedBillsQueryHandler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<GetTrackedBillsQuery, List<TrackedBillModel>>
    {
        public async Task<List<TrackedBillModel>> Handle(GetTrackedBillsQuery request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId!;

            var billTrackings = await context.BillTrackings.AsNoTracking()
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync(cancellationToken);

            return billTrackings.Select(b => b.ToModel()).ToList();
        }
    }
}
