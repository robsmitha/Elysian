using System.Text.RegularExpressions;
using AutoMapper;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class InstitutionModel
    {
        public string institution_id { get; set; }
        public string name { get; set; }
        /// <summary>Base64 PNG for display, when Plaid has one</summary>
        public string? logo { get; set; }
        /// <summary>The bank's brand color as #RRGGBB, when Plaid has one</summary>
        public string? primary_color { get; set; }

        private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
        private const int MaxLogoLength = 200_000;

        /// <summary>A stand-in when Plaid's institution lookup fails, so one bad lookup doesn't hide the bank.</summary>
        public static InstitutionModel Unknown(string institutionId) => new() { institution_id = institutionId, name = "Your bank" };

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                // Display-only values from Plaid are checked before they reach the page
                CreateMap<InstitutionResponse.Institution, InstitutionModel>()
                    .ForMember(d => d.logo, o => o.MapFrom(s => s.logo != null && s.logo.Length <= MaxLogoLength ? s.logo : null))
                    .ForMember(d => d.primary_color, o => o.MapFrom(s => s.primary_color != null && HexColor.IsMatch(s.primary_color) ? s.primary_color : null));
            }
        }
    }
}
