using Elysian.Application.Features.Congress.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Congress.Commands
{
    /// <summary>
    /// Replaces the user's private note on a saved bill. Empty clears it.
    /// </summary>
    [Authorize]
    public record SaveTrackedBillNotesCommand(int BillTrackingId, string? Notes) : IRequest<TrackedBillModel>;

    public class SaveTrackedBillNotesCommandValidator : AbstractValidator<SaveTrackedBillNotesCommand>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public SaveTrackedBillNotesCommandValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        {
            _context = context;
            _claimsPrincipalAccessor = claimsPrincipalAccessor;

            RuleFor(v => v.BillTrackingId)
                .NotEmpty()
                .MustAsync(BelongsToUserAsync)
                    .WithMessage("The saved bill does not belong to this user.");

            RuleFor(v => v.Notes).MaximumLength(4000);
        }

        public async Task<bool> BelongsToUserAsync(int billTrackingId, CancellationToken cancellationToken)
        {
            return await _context.BillTrackings.AnyAsync(b => b.BillTrackingId == billTrackingId
                && b.UserId == _claimsPrincipalAccessor.UserId, cancellationToken);
        }
    }

    public class SaveTrackedBillNotesCommandHandler(ElysianContext context)
        : IRequestHandler<SaveTrackedBillNotesCommand, TrackedBillModel>
    {
        public async Task<TrackedBillModel> Handle(SaveTrackedBillNotesCommand request, CancellationToken cancellationToken)
        {
            var billTracking = await context.BillTrackings.SingleAsync(b => b.BillTrackingId == request.BillTrackingId, cancellationToken);

            billTracking.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

            await context.SaveChangesAsync(cancellationToken);

            return billTracking.ToModel();
        }
    }
}
