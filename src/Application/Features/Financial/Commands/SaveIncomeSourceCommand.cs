using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize(Policy = PolicyNames.IncomeWrite)]
    public record SaveIncomeSourceCommand(IncomeSourceModel IncomeSource) : IRequest<IncomeSourceModel>;
    
    public class SaveIncomeSourceCommandValidator : AbstractValidator<SaveIncomeSourceCommand>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public SaveIncomeSourceCommandValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        {
            _context = context;
            _claimsPrincipalAccessor = claimsPrincipalAccessor;

            RuleFor(v => v.IncomeSource)
                .NotEmpty()
                .Must(HaveValidDateRange)
                    .WithMessage("Start Date must be before End Date.");

            RuleFor(v => v.IncomeSource)
                .NotEmpty()
                .MustAsync(HaveUniqueNameAsync)
                    .WithMessage("Income source name already exists.");


            RuleFor(v => v.IncomeSource.InstitutionAccessItemId)
                .NotEmpty()
                .MustAsync(BelongsToUserAsync)
                    .WithMessage("The financial account does not belong to this user.");
        }

        public bool HaveValidDateRange(IncomeSourceModel incomeSource)
        {
            return !incomeSource.EndDate.HasValue || incomeSource.StartDate <= incomeSource.EndDate;
        }

        public async Task<bool> HaveUniqueNameAsync(IncomeSourceModel incomeSource,
            CancellationToken cancellationToken)
        {
            return await _context.IncomeSources.AllAsync(c => !string.Equals(c.Name, incomeSource.Name)
                || (c.Name  == incomeSource.Name && c.IncomeSourceId == incomeSource.IncomeSourceId), cancellationToken: cancellationToken);
        }

        public async Task<bool> BelongsToUserAsync(int institutionAccessItemId,
            CancellationToken cancellationToken)
        {
            return await _context.InstitutionAccessItemUsers.AnyAsync(i => i.InstitutionAccessItemId == institutionAccessItemId 
                && i.User.ExternalUserId == _claimsPrincipalAccessor.UserId, cancellationToken: cancellationToken);
        }
    }

    public class SaveIncomeSourceCommandHandler(IIncomeService incomeService) : IRequestHandler<SaveIncomeSourceCommand, IncomeSourceModel>
    {
        public async Task<IncomeSourceModel> Handle(SaveIncomeSourceCommand request, CancellationToken cancellationToken)
        {
            IncomeSourceModel model;
            if (request.IncomeSource.IsExisting)
            {
                model = await incomeService.UpdateIncomeSourceAsync(request.IncomeSource);
            }
            else
            {
                model = await incomeService.AddIncomeSourceAsync(request.IncomeSource);
            }

            return model;
        }
    }
}
