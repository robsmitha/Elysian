using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Congress.Commands
{
    /// <summary>
    /// Records that the user opened a saved bill, which clears its new activity flag.
    /// </summary>
    [Authorize]
    public record MarkTrackedBillViewedCommand(int BillTrackingId) : IRequest;

    public class MarkTrackedBillViewedCommandValidator : AbstractValidator<MarkTrackedBillViewedCommand>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public MarkTrackedBillViewedCommandValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        {
            _context = context;
            _claimsPrincipalAccessor = claimsPrincipalAccessor;

            RuleFor(v => v.BillTrackingId)
                .NotEmpty()
                .MustAsync(BelongsToUserAsync)
                    .WithMessage("The saved bill does not belong to this user.");
        }

        public async Task<bool> BelongsToUserAsync(int billTrackingId, CancellationToken cancellationToken)
        {
            return await _context.BillTrackings.AnyAsync(b => b.BillTrackingId == billTrackingId
                && b.UserId == _claimsPrincipalAccessor.UserId, cancellationToken);
        }
    }

    public class MarkTrackedBillViewedCommandHandler(ElysianContext context, TimeProvider timeProvider)
        : IRequestHandler<MarkTrackedBillViewedCommand>
    {
        public async Task Handle(MarkTrackedBillViewedCommand request, CancellationToken cancellationToken)
        {
            var billTracking = await context.BillTrackings.SingleAsync(b => b.BillTrackingId == request.BillTrackingId, cancellationToken);

            billTracking.LastViewedAt = timeProvider.GetUtcNow();
            billTracking.HasUnseenActivity = false;

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
