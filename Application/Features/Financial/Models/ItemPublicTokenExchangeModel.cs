using AutoMapper;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class ItemPublicTokenExchangeModel
    {
        public string access_token { get; set; }
        public string item_id { get; set; }
        public string request_id { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<ItemPublicTokenExchangeResponse, ItemPublicTokenExchangeModel>();
            }
        }
    }
}
