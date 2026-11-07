using AutoMapper;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class AccountModel
    {
        public string account_id { get; set; }
        public string mask { get; set; }
        public string name { get; set; }
        public string official_name { get; set; }
        public string subtype { get; set; }
        public string type { get; set; }
        public BalancesModel? balances { get; set; }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<Account, AccountModel>();
                CreateMap<Balances, BalancesModel>();
            }
        }
    }

    /// <summary>Balances as Plaid reports them from /accounts/get (cached by Plaid, not a live balance check)</summary>
    public class BalancesModel
    {
        public decimal? available { get; set; }
        public decimal? current { get; set; }
        public decimal? limit { get; set; }
        public string? iso_currency_code { get; set; }
    }
}
