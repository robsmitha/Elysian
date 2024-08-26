using AutoMapper;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.Financial.Models
{
    public class BudgetModel
    {
        public int BudgetId { get; set; }
        public string Name { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<FinancialCategoryModel> Categories { get; set; }
        public List<InstitutionAccessItemModel> BudgetAccessItems { get; set; }

        public bool IsExisting => BudgetId > 0;

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<Budget, BudgetModel>();
            }
        }
    }
}
