using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.IncomeRead)]
    public record GetIncomeSourcesQuery(int InstitutionAccessItemId) : IRequest<List<IncomeSourceListItem>>;

    public class IncomeSourceListItem(IncomeSourceModel incomeSource, List<IncomePaymentModel> incomePayments)
    {
        public IncomeSourceModel IncomeSource { get; set; } = incomeSource;
        public List<IncomePaymentModel> IncomePayments { get; set; } = incomePayments;

        public decimal CurrentMonthPaymentTotal
        {
            get
            {
                var now = DateTime.UtcNow;
                var startOfMonth = new DateTime(now.Year, now.Month, 1);
                var endOfMonth = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)).AddDays(1).AddTicks(-1);
                
                return IncomePayments
                    .Where(p => p.PaymentDate >= startOfMonth && p.PaymentDate <= endOfMonth)
                    .Sum(p => p.Amount);
            }
        }

        public bool CurrentMonthPaid => CurrentMonthPaymentTotal >= IncomeSource.AmountDue;

        public bool CurrentMonthPastDue
        {
            get
            {
                if (!IncomeSource.DayOfMonthDue.HasValue)
                {
                    return !CurrentMonthPaid;
                }

                var now = DateTime.UtcNow;
                var dueDate = new DateTime(now.Year, now.Month, IncomeSource.DayOfMonthDue.Value);

                return now > dueDate && !CurrentMonthPaid;
            }
        }

        public record MonthlyPayment(string Month, int Year, decimal PaidAmount, decimal AmountDue);
        public List<MonthlyPayment> PaymentHistory =>
            [.. Enumerable.Range(0, 12)
                .Select(offset =>
                {
                    var monthDate = DateTime.UtcNow.AddMonths(-offset);
                    var year = monthDate.Year;
                    var month = monthDate.Month;

                    var monthName = new DateTime(year, month, 1).ToString("MMMM");

                    var amount = IncomePayments
                        .Where(p => p.PaymentDate.Year == year && p.PaymentDate.Month == month)
                        .Sum(p => p.Amount);

                    return new MonthlyPayment(monthName, year, amount, IncomeSource.AmountDue);
                })
                .OrderBy(mp => new DateTime(mp.Year, DateTime.ParseExact(mp.Month, "MMMM", null).Month, 1))];
    }

    public class GetIncomeSourcesQueryValidator : AbstractValidator<GetIncomeSourcesQuery>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _claimsPrincipalAccessor;
        public GetIncomeSourcesQueryValidator(ElysianContext context, IClaimsPrincipalAccessor claimsPrincipalAccessor)
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

    public class GetIncomeSourcesQueryHandler(IIncomeService incomeService) 
        : IRequestHandler<GetIncomeSourcesQuery, List<IncomeSourceListItem>>
    {
        public async Task<List<IncomeSourceListItem>> Handle(GetIncomeSourcesQuery request, CancellationToken cancellationToken)
        {
            var incomeSourceList = new List<IncomeSourceListItem>();

            var incomeSources = await incomeService.GetIncomeSourcesAsync(request.InstitutionAccessItemId);
            foreach (var incomeSource in incomeSources)
            {
                var item = await GetIncomeSourceItemAsync(incomeSource);

                incomeSourceList.Add(item);
            }
            return incomeSourceList;
        }

        private async Task<IncomeSourceListItem> GetIncomeSourceItemAsync(IncomeSourceModel incomeSource)
        {
            var incomePayments = await incomeService.GetIncomePaymentsAsync(incomeSource.IncomeSourceId);

            return new IncomeSourceListItem(incomeSource, incomePayments);
        }
    }
}
