using System.Text.Json.Serialization;
using AutoMapper;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.Financial.Models
{
    public class FinancialCategoryModel
    {
        public int FinancialCategoryId { get; set; }
        public string Name { get; set; }
        /// <summary>A default category's stable key; null for ones the person made</summary>
        public string? SystemKey { get; set; }
        public string? Icon { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CategoryGroup Group { get; set; } = CategoryGroup.Flexible;
        public bool IsArchived { get; set; }

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
