using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.IncomeRead)]
    public record GetIncomeSourceQuery(int IncomeSourceId) : IRequest<IncomeSourceModel>;

    public class GetIncomeSourceQueryValidator : AbstractValidator<GetIncomeSourceQuery>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public GetIncomeSourceQueryValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
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
                && i.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == _claimsPrincipalAccessor.UserId));
        }
    }

    public class GetIncomeSourceQueryHandler(IIncomeService incomeService)
        : IRequestHandler<GetIncomeSourceQuery, IncomeSourceModel>
    {
        public async Task<IncomeSourceModel> Handle(GetIncomeSourceQuery request, CancellationToken cancellationToken)
        {
            return await incomeService.GetIncomeSourceAsync(request.IncomeSourceId);
        }
    }
}
