using AutoMapper;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.Financial.Models
{
    public class BudgetExcludedTransactionModel
    {
        public string TransactionId { get; set; }
        public int BudgetId { get; set; }
        public string BudgetName { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<BudgetExcludedTransaction, BudgetExcludedTransactionModel>();
            }
        }
    }
}
