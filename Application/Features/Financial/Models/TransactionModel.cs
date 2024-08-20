using AutoMapper;
using Elysian.Domain.Data;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class TransactionModel
    {
        public string date { get; set; }
        public string name { get; set; }
        public double amount { get; set; }
        public string transaction_id { get; set; }
        public bool pending { get; set; }
        public string authorized_date { get; set; }
        public string transaction_type { get; set; }
        public string payment_channel { get; set; }
        public string merchant_name { get; set; }
        public string account_id { get; set; }

        public bool HasTransactionCategory => Category != null;
        public FinancialCategoryModel Category { get; set; }
        public AccountModel Account { get; set; }


        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<TransactionResponse.Transaction, TransactionModel>()
                    .ForMember(dest => dest.Account, act => act.Ignore())
                    .ForMember(dest => dest.Category, act => act.Ignore());
            }
        }
    }
}
