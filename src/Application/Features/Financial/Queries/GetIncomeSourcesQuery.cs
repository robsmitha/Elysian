using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Security;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Queries
{
    [Authorize(Policy = PolicyNames.IncomeRead)]
    public record GetIncomeSourcesQuery : IRequest<List<IncomeSourceListItem>>;

    public class IncomeSourceListItem(IncomeSourceModel incomeSource, List<IncomePaymentModel> incomePayments)
    {
        public IncomeSourceModel IncomeSource { get; set; } = incomeSource;
        public List<IncomePaymentModel> IncomePayments { get; set; } = incomePayments;
    }

    public class GetIncomeSourcesQueryHandler(IClaimsPrincipalAccessor claimsPrincipalAccessor, IIncomeService incomeService) 
        : IRequestHandler<GetIncomeSourcesQuery, List<IncomeSourceListItem>>
    {
        public async Task<List<IncomeSourceListItem>> Handle(GetIncomeSourcesQuery request, CancellationToken cancellationToken)
        {
            var incomeSourceList = new List<IncomeSourceListItem>();

            var incomeSources = await incomeService.GetIncomeSourcesAsync(claimsPrincipalAccessor.UserId);
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
