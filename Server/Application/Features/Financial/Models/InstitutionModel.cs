using AutoMapper;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class InstitutionModel
    {
        public string institution_id { get; set; }
        public string name { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<InstitutionResponse.Institution, InstitutionModel>();
            }
        }
    }
}
