using AutoMapper;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.Financial.Models
{
    /// <summary>A bank connection with its decrypted access token. Server-only: never returned to the browser.</summary>
    public class InstitutionAccessTokenModel
    {
        public int InstitutionAccessItemId { get; set; }
        public string UserId { get; set; }
        /// <summary>Null for a bank that was removed (its access was revoked at Plaid)</summary>
        public string? AccessToken { get; set; }
        public string ItemId { get; set; }
        public string InstitutionId { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                // The token is decrypted by AccessTokenService, never copied straight from the entity
                CreateMap<InstitutionAccessItem, InstitutionAccessTokenModel>()
                    .ForMember(d => d.AccessToken, o => o.Ignore())
                    .ForMember(d => d.UserId, o => o.Ignore());
            }
        }
    }
}
