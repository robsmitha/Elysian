using AutoMapper;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class ItemModel
    {
        public string ItemId { get; set; }
        public string InstitutionId { get; set; }
        public DateTime? LastSuccessfulUpdate { get; set; }
        public string ErrorDisplayMessage { get; set; }
        public string ErrorCode { get; set; }
        public bool HasError => !string.IsNullOrWhiteSpace(ErrorCode);


        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<ItemResponse, ItemModel>()
                       .ForMember(dest => dest.ItemId, act => act.MapFrom(a => a.item.item_id))
                       .ForMember(dest => dest.InstitutionId, act => act.MapFrom(a => a.item.institution_id))
                       .ForMember(dest => dest.ErrorDisplayMessage, act => act.MapFrom(a => a.item.error != null ? a.item.error.error_message : string.Empty))
                       .ForMember(dest => dest.ErrorCode, act => act.MapFrom(a => a.item.error != null ? a.item.error.error_code : string.Empty))
                       .ForMember(dest => dest.LastSuccessfulUpdate, act => act.MapFrom(a => a.status.transactions.last_successful_update));
            }
        }
    }
}
