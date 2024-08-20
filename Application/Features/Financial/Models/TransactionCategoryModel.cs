using AutoMapper;
using Elysian.Domain.Data;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class TransactionCategoryModel
    {
        public int TransactionCategoryId { get; set; }
        public string TransactionId { get; set; }
        public int FinancialCategoryId { get; set; }
        public string FinancialCategoryName { get; set; }
        public int BudgetId { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<TransactionCategory, TransactionCategoryModel> ();
            }
        }
    }
}
