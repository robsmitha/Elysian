using AutoMapper;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.Financial.Models
{
    public class InstitutionAccessTokenModel
    {
        public int InstitutionAccessItemId { get; set; }
        public string UserId { get; set; }
        public string AccessToken { get; set; }
        public string ItemId { get; set; }
        public string InstitutionId { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<InstitutionAccessItem, InstitutionAccessTokenModel>();
            }
        }
    }
}
