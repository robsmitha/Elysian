using AutoMapper;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class LinkTokenModel
    {
        public DateTime expiration { get; set; }
        public string link_token { get; set; }
        public string request_id { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<CreateLinkTokenResponse, LinkTokenModel>();
            }
        }
    }
}
