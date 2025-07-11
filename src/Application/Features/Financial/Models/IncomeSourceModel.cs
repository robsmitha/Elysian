﻿using AutoMapper;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Models
{
    public class IncomeSourceModel
    {
        public int IncomeSourceId { get; set; }
        public int InstitutionAccessItemId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal AmountDue { get; set; }
        public int? DayOfMonthDue { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IncomeSourceType IncomeSourceType { get; set; }
        public List<ExpectedPaymentMemo> ExpectedPaymentMemos { get; set; }

        public bool IsExisting => IncomeSourceId > 0;

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<IncomeSource, IncomeSourceModel>().ReverseMap();
            }
        }
    }
}
