using AutoMapper;
using Elysian.Domain.Data;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class BudgetCategoryModel
    {
        public int Id { get; set; }
        public decimal Estimate { get; set; }
        public int BudgetId { get; set; }
        public int FinancialCategoryId { get; set; }
        public string FinancialCategoryName { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<BudgetCategory, BudgetCategoryModel>();
            }
        }
    }
}
