using Elysian.Application.Features.Financial.Models;
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
    [Authorize(Policy = PolicyNames.IncomeWrite)]
    public record SaveIncomePaymentCommand(IncomePaymentModel IncomePayment) : IRequest<IncomePaymentModel>;

    public class SaveIncomePaymentCommandValidator : AbstractValidator<SaveIncomePaymentCommand>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public SaveIncomePaymentCommandValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        {
            _context = context;
            _claimsPrincipalAccessor = claimsPrincipalAccessor;

            RuleFor(v => v.IncomePayment.IncomeSourceId)
                .NotEmpty()
                .MustAsync(BelongsToUserAsync)
                    .WithMessage("The financial account does not belong to this user.");
        }

        public async Task<bool> BelongsToUserAsync(int incomeSourceId,
            CancellationToken cancellationToken)
        {
            return await _context.IncomeSources.AnyAsync(i => i.IncomeSourceId == incomeSourceId
                && i.InstitutionAccessItem.AccessUsers.Any(a => a.User.ExternalUserId == _claimsPrincipalAccessor.UserId), cancellationToken: cancellationToken);
        }
    }

    public class SaveIncomePaymentCommandHandler(IIncomeService incomeService) : IRequestHandler<SaveIncomePaymentCommand, IncomePaymentModel>
    {
        public async Task<IncomePaymentModel> Handle(SaveIncomePaymentCommand request, CancellationToken cancellationToken)
        {
            IncomePaymentModel model;
            if (request.IncomePayment.IsExisting)
            {
                model = await incomeService.UpdateIncomePaymentAsync(request.IncomePayment);
            }
            else
            {
                if (!string.IsNullOrEmpty(request.IncomePayment.TransactionId))
                {
                    await incomeService.DeletePaymentByTransactionIdAsync(request.IncomePayment.TransactionId);
                }

                model = await incomeService.AddIncomePaymentAsync(request.IncomePayment);
            }

            return model;
        }
    }
}
