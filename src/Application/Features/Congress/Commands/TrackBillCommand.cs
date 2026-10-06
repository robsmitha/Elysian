using CapitolSharp.Congress;
using CapitolSharp.Congress.Bills;
using CapitolSharp.Congress.Enums;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Congress.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Congress.Commands
{
    /// <summary>
    /// Saves a bill for the signed in user. Saving one that's already saved returns it unchanged.
    /// </summary>
    [Authorize]
    public record TrackBillCommand(int Congress, string BillType, int BillNumber) : IRequest<TrackedBillModel>;

    public class TrackBillCommandValidator : AbstractValidator<TrackBillCommand>
    {
        public TrackBillCommandValidator()
        {
            // Congress.gov's bill data starts with the 93rd Congress (1973).
            RuleFor(v => v.Congress).GreaterThanOrEqualTo(93);

            RuleFor(v => v.BillType)
                .NotEmpty()
                .Must(BeBillType)
                    .WithMessage("Bill type must be one of: " + string.Join(", ", Enum.GetNames<BillType>()));

            RuleFor(v => v.BillNumber).GreaterThan(0);
        }

        // Enum.TryParse also accepts numbers, so match on the names.
        public static bool BeBillType(string billType)
        {
            return Enum.GetNames<BillType>().Contains(billType, StringComparer.OrdinalIgnoreCase);
        }
    }

    public class TrackBillCommandHandler(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor,
        CapitolSharpCongress congressClient, TimeProvider timeProvider)
        : IRequestHandler<TrackBillCommand, TrackedBillModel>
    {
        public async Task<TrackedBillModel> Handle(TrackBillCommand request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId!;
            var billType = request.BillType.ToUpperInvariant();

            var existing = await FindAsync(userId, request.Congress, billType, request.BillNumber, cancellationToken);
            if (existing != null)
            {
                return existing.ToModel();
            }

            var response = await congressClient.SendAsync(new BillDetailsRequest
            {
                Congress = request.Congress,
                BillType = Enum.Parse<BillType>(billType, ignoreCase: true),
                BillNumber = request.BillNumber
            });

            var bill = response?.Bill
                ?? throw new NotFoundException(nameof(BillTracking), $"{request.Congress} {billType} {request.BillNumber}");

            var billTracking = new BillTracking
            {
                UserId = userId,
                Congress = request.Congress,
                BillType = billType,
                BillNumber = request.BillNumber
            };
            billTracking.ApplySnapshot(bill, timeProvider.GetUtcNow());

            context.BillTrackings.Add(billTracking);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A second click can save the same bill first; the unique index stops the duplicate.
                context.Entry(billTracking).State = EntityState.Detached;
                existing = await FindAsync(userId, request.Congress, billType, request.BillNumber, cancellationToken);
                if (existing == null)
                {
                    throw;
                }
                return existing.ToModel();
            }

            return billTracking.ToModel();
        }

        private Task<BillTracking?> FindAsync(string userId, int congress, string billType, int billNumber,
            CancellationToken cancellationToken)
        {
            return context.BillTrackings.AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId
                    && b.Congress == congress
                    && b.BillType == billType
                    && b.BillNumber == billNumber, cancellationToken);
        }
    }
}
