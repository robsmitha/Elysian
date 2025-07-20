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
    public record GetIncomeSourcesQuery(int InstitutionAccessItemId, int? Month, int? Year) : IRequest<GetIncomeSourcesResponse>;

    public class GetIncomeSourcesResponse(List<IncomeSourceListItem> incomeSources, int month, int year)
    {
        public List<IncomeSourceListItem> IncomeSources { get; set; } = incomeSources;
        private int Month { get; set; } = month;
        private int Year { get; set; } = year;
        public MonthlyTimelineListItem MonthlyTimeline => new(new DateTime(Year, Month, 1).ToString("MMMM"), Month, Year);
        public List<MonthlyTimelineListItem> MonthlyTimelineList =>
            [.. Enumerable.Range(0, 12)
            .Select(offset =>
            {
                var dateValue = DateTime.UtcNow.AddMonths(-offset);
                var year = dateValue.Year;
                var month = dateValue.Month;
                var monthName = new DateTime(year, month, 1).ToString("MMMM");
                return new MonthlyTimelineListItem($"{monthName} {year}", month, year);
            })
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)];


        private List<IncomePaymentModel> MonthlyTimelinePayments => [.. IncomeSources.SelectMany(p => p.IncomePayments)
            .Where(p => p.PaymentDate.Month == Month && p.PaymentDate.Year == Year)];

        public decimal TotalPaid => MonthlyTimelinePayments.Sum(d => d.Amount);
        public decimal TotalDue => IncomeSources.Sum(d => d.IncomeSource.AmountDue);
        public decimal TotalOverdue => IncomeSources.Where(i => !i.CurrentMonthPaid).Sum(d => d.IncomeSource.AmountDue);
        public DateTime? NextDueDate => IncomeSources.Select(d => d.DueDate)
            .Where(d => d.Date >= DateTime.Today).OrderBy(d => d).FirstOrDefault();
    }

    public class IncomeSourceListItem(IncomeSourceModel incomeSource, List<IncomePaymentModel> incomePayments, int month, int year)
    {
        public IncomeSourceModel IncomeSource { get; set; } = incomeSource;
        public List<IncomePaymentModel> IncomePayments { get; set; } = incomePayments;

        private int Month { get; set; } = month;
        private int Year { get; set; } = year;

        public MonthlyTimelineListItem MonthlyTimeline => new(new DateTime(Year, Month, 1).ToString("MMMM"), Month, Year);
        public DateTime DueDate
        {
            get
            {
                if (!IncomeSource.DayOfMonthDue.HasValue)
                {
                    return new DateTime(Year, Month, 1);
                }

                return new DateTime(Year, Month, IncomeSource.DayOfMonthDue.Value);
            }
        }

        public DateTime PastDueDate => DueDate.AddDays(3);

        public decimal CurrentMonthPaymentTotal
        {
            get
            {
                var startOfMonth = new DateTime(Year, Month, 1);
                var endOfMonth = new DateTime(Year, Month, DateTime.DaysInMonth(Year, Month)).AddDays(1).AddTicks(-1);
                
                return IncomePayments
                    .Where(p => p.PaymentDate >= startOfMonth && p.PaymentDate <= endOfMonth)
                    .Sum(p => p.Amount);
            }
        }

        public bool CurrentMonthPaid => CurrentMonthPaymentTotal >= IncomeSource.AmountDue;

        public bool CurrentMonthPastDue => DateTime.UtcNow > PastDueDate && !CurrentMonthPaid;

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

                    var dueDate = new DateTime(year, month, DueDate.Day);
                    return new MonthlyPayment(monthName, year, amount, IncomeSource.AmountDue, dueDate);
                })
                .OrderByDescending(mp => new DateTime(mp.Year, DateTime.ParseExact(mp.Month, "MMMM", null).Month, 1))];
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
        : IRequestHandler<GetIncomeSourcesQuery, GetIncomeSourcesResponse>
    {
        public async Task<GetIncomeSourcesResponse> Handle(GetIncomeSourcesQuery request, CancellationToken cancellationToken)
        {
            var incomeSourceList = new List<IncomeSourceListItem>();

            var now = DateTime.UtcNow;
            var month = request.Month ?? now.Month;
            var year = request.Year ?? now.Year;

            var incomeSources = await incomeService.GetIncomeSourcesAsync(request.InstitutionAccessItemId);
            foreach (var incomeSource in incomeSources)
            {
                var incomePayments = await incomeService.GetIncomePaymentsAsync(incomeSource.IncomeSourceId);

                incomeSourceList.Add(new(incomeSource, incomePayments, month, year));
            }

            return new (incomeSourceList, month, year);
        }
    }
}
