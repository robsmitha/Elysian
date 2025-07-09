using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.IncomeRead)]
    public record GetTransactionsQuery(int InstitutionAccessItemId) : IRequest<GetTransactionsResponse>;

    public class GetTransactionsResponse(List<TransactionModel> transactions, bool expired)
    {
        public List<TransactionModel> Transactions { get; set; } = transactions;
        public bool Expired { get; set; } = expired;
    }

    public class GetTransactionsQueryValidator : AbstractValidator<GetTransactionsQuery>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public GetTransactionsQueryValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        {
            _context = context;
            _claimsPrincipalAccessor = claimsPrincipalAccessor;

            RuleFor(v => v.InstitutionAccessItemId)
                .NotEmpty()
                .MustAsync(BelongsToUserAsync)
                    .WithMessage("The financial account does not belong to this user.");
        }

        public async Task<bool> BelongsToUserAsync(int institutionAccessItemId,
            CancellationToken cancellationToken)
        {
            return await _context.InstitutionAccessItemUsers.AnyAsync(i => i.InstitutionAccessItemId == institutionAccessItemId
                && i.User.ExternalUserId == _claimsPrincipalAccessor.UserId, cancellationToken: cancellationToken);
        }
    }

    public class GetTransactionsQueryHandler(IClaimsPrincipalAccessor claimsPrincipalAccessor, IAccessTokenService accessTokenService,
        IFinancialService financialService)
        : IRequestHandler<GetTransactionsQuery, GetTransactionsResponse>
    {
        public async Task<GetTransactionsResponse> Handle(GetTransactionsQuery request, CancellationToken cancellationToken)
        {
            var accessToken = await accessTokenService.GetAccessTokenAsync(claimsPrincipalAccessor.UserId, request.InstitutionAccessItemId);
            var transactions = new List<TransactionModel>();
            var expired = false;
            try
            {
                var endDate = DateTime.UtcNow;
                var startDate = endDate.AddMonths(-24);

                transactions = await financialService.GetTransactionsAsync(accessToken.AccessToken, startDate, endDate);
            }
            catch (FinancialServiceException fex)
            {
                if (string.Equals(fex.Error?.error_code, PlaidErrorCodes.ITEM_LOGIN_REQUIRED,
                           StringComparison.InvariantCultureIgnoreCase))
                {
                    expired = true;
                }
            }

            return new GetTransactionsResponse(transactions, expired);
        }
    }
}
