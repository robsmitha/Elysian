using System.Text.Json.Serialization;
using AutoMapper;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Responses.Plaid;

namespace Elysian.Application.Features.Financial.Models
{
    public class TransactionSplitModel
    {
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public decimal Amount { get; set; }
        public string? Notes { get; set; }
    }

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

        // Stored transactions (Phase 2): our id, what it counts as, and who decided
        public long Id { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Treatment Treatment { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ClassificationSource TreatmentSource { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ClassificationSource CategorySource { get; set; }
        public string? Notes { get; set; }
        public List<TransactionSplitModel> Splits { get; set; } = [];
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AccountBudgetRole AccountRole { get; set; }
        /// <summary>Plaid's detailed category code</summary>
        public string? PlaidCategory { get; set; }
        public string? MerchantKey { get; set; }

        /// <summary>Counted in budgets: spending or a refund, on an account that counts as spending</summary>
        public bool IsCounted => AccountRole == AccountBudgetRole.Spending && Treatment is Treatment.Spending or Treatment.Refund;
        public IncomePaymentModel IncomePayment { get; set; }
        public MonthlyTimelineListItem? MonthlyTimeline
        {
            get
            {
                if(!DateTime.TryParse(date, out var dateValue))
                {
                    return null;
                }

                var year = dateValue.Year;
                var month = dateValue.Month;

                var monthName = new DateTime(year, month, 1).ToString("MMMM");
                return new MonthlyTimelineListItem($"{monthName} {year}", month, year);
            }
        }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<TransactionResponse.Transaction, TransactionModel>()
                    .ForMember(dest => dest.Account, act => act.Ignore())
                    .ForMember(dest => dest.Category, act => act.Ignore())
                    .ForMember(dest => dest.IncomePayment, act => act.Ignore());
            }
        }
    }
}
