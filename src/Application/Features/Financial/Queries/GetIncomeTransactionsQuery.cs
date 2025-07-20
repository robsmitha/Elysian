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
    public record GetIncomeTransactionsQuery(int InstitutionAccessItemId, 
        DateTime? StartDate = null, DateTime? EndDate = null) : IRequest<GetIncomeTransactionsResponse>;

    public class GetIncomeTransactionsResponse(List<TransactionModel> transactions, bool expired, string institutionName)
    {
        public List<TransactionModel> Transactions { get; set; } = transactions;
        public bool Expired { get; set; } = expired;
        public string InstitutionName { get; set; } = institutionName;
    }

    public class GetIncomeTransactionsQueryValidator : AbstractValidator<GetIncomeTransactionsQuery>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public GetIncomeTransactionsQueryValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        {
            _context = context;
            _claimsPrincipalAccessor = claimsPrincipalAccessor;

            RuleFor(v => v.InstitutionAccessItemId)
                .NotEmpty()
                .MustAsync(BelongsToUserAsync)
                    .WithMessage("The financial account does not belong to this user.");
            
            RuleFor(v => v)
                .Must(query =>
                    (query.StartDate.HasValue && query.EndDate.HasValue) ||
                    (!query.StartDate.HasValue && !query.EndDate.HasValue)
                )
                .WithMessage("StartDate and EndDate must both be provided or both be null.");

            RuleFor(v => v)
                .Must(query =>
                {
                    if (query.StartDate == null || query.EndDate == null)
                        return true;

                    return query.EndDate <= query.StartDate.Value.AddYears(2);
                })
                .WithMessage("The date range cannot exceed 2 years.");
        }

        public async Task<bool> BelongsToUserAsync(int institutionAccessItemId,
            CancellationToken cancellationToken)
        {
            return await _context.InstitutionAccessItemUsers.AnyAsync(i => i.InstitutionAccessItemId == institutionAccessItemId
                && i.User.ExternalUserId == _claimsPrincipalAccessor.UserId, cancellationToken: cancellationToken);
        }
    }

    public class GetIncomeTransactionsQueryHandler(IClaimsPrincipalAccessor claimsPrincipalAccessor, IAccessTokenService accessTokenService,
        IFinancialService financialService, IIncomeService incomeService)
        : IRequestHandler<GetIncomeTransactionsQuery, GetIncomeTransactionsResponse>
    {
        public async Task<GetIncomeTransactionsResponse> Handle(GetIncomeTransactionsQuery request, CancellationToken cancellationToken)
        {
            var accessToken = await accessTokenService.GetAccessTokenAsync(claimsPrincipalAccessor.UserId, request.InstitutionAccessItemId);
            var transactions = new List<TransactionModel>();
            var expired = false;
            var institution = await financialService.GetInstitutionAsync(accessToken.InstitutionId);
            try
            {

                var endDate = request.EndDate ?? DateTime.UtcNow;
                var startDate = request.StartDate ?? endDate.AddMonths(-24);

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

            var incomePayments = await incomeService.GetIncomePaymentsByInstitutionAccessItemIdAsync(request.InstitutionAccessItemId);

            var paymentData = from t in transactions
                                    join p in incomePayments on t.transaction_id equals p.TransactionId into tmp
                                    from p in tmp.DefaultIfEmpty()
                                    select new
                                    {
                                        Transaction = t,
                                        IncomePayment = p
                                    };
            var transactionData = paymentData.Select(d =>
            {
                var transaction = d.Transaction;
                transaction.IncomePayment = d.IncomePayment;
                return transaction;
            });

            return new GetIncomeTransactionsResponse([..transactionData], expired, institution.name);
        }
    }
}
