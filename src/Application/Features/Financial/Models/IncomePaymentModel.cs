using AutoMapper;
using Elysian.Domain.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Models
{
    public class IncomePaymentModel
    {
        public int IncomePaymentId { get; set; }
        public string TransactionId { get; set; }
        public int IncomeSourceId { get; set; }
        public DateTime PaymentDate { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMemo { get; set; }
        public bool IsManualAdjustment { get; set; }
        public bool IsExisting { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<IncomePayment, IncomePaymentModel>();
            }
        }
    }
}
