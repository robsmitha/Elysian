using AutoMapper;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.Financial.Models
{
    public class FinancialCategoryModel
    {
        public int FinancialCategoryId { get; set; }
        public string Name { get; set; }


        public bool IsExisting => FinancialCategoryId > 0;

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<FinancialCategory, FinancialCategoryModel>();
            }
        }
    }
}
