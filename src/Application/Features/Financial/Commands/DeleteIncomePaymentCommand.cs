using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize(Policy = PolicyNames.IncomeDelete)]
    public record DeleteIncomePaymentCommand(int IncomePaymentId) : IRequest;

    public class DeleteIncomePaymentCommandValidator : AbstractValidator<DeleteIncomePaymentCommand>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public DeleteIncomePaymentCommandValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        {
            _context = context;
            _claimsPrincipalAccessor = claimsPrincipalAccessor;

            RuleFor(v => v.IncomePaymentId)
                .NotEmpty()
                .MustAsync(BelongsToUserAsync)
                    .WithMessage("The financial account does not belong to this user.");
        }

        public async Task<bool> BelongsToUserAsync(int incomePaymentId,
            CancellationToken cancellationToken)
        {
            return await _context.IncomePayments
                .AnyAsync(i => i.IncomePaymentId == incomePaymentId
                && i.IncomeSource.InstitutionAccessItem.AccessUsers.Any(a => a.User.ExternalUserId == _claimsPrincipalAccessor.UserId), cancellationToken: cancellationToken);
        }
    }
    public class DeleteIncomePaymentCommandHandler(IIncomeService incomeService) : IRequestHandler<DeleteIncomePaymentCommand>
    {
        public async Task Handle(DeleteIncomePaymentCommand request, CancellationToken cancellationToken)
        {
            await incomeService.DeleteIncomePaymentAsync(request.IncomePaymentId);
        }
    }
}
