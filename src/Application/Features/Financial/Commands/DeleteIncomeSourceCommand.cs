using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize(Policy = PolicyNames.IncomeDelete)]
    public record DeleteIncomeSourceCommand(int IncomeSourceId) : IRequest;

    public class DeleteIncomeSourceCommandValidator : AbstractValidator<DeleteIncomeSourceCommand>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public DeleteIncomeSourceCommandValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        {
            _context = context;
            _claimsPrincipalAccessor = claimsPrincipalAccessor;

            RuleFor(v => v.IncomeSourceId)
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
    public class DeleteIncomeSourceCommandHandler(IncomeService incomeService) : IRequestHandler<DeleteIncomeSourceCommand>
    {
        public async Task Handle(DeleteIncomeSourceCommand request, CancellationToken cancellationToken) 
        { 
            await incomeService.DeleteIncomeSourceAsync(request.IncomeSourceId);
        }
    }
}
