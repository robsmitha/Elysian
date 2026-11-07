using Elysian.Application.Features.Financial.Models;
using Elysian.Domain.Data;

namespace Elysian.Infrastructure.Services
{
    /// <summary>Stored transaction → the shape the pages read. Needs FinancialAccount, Category and Splits(.Category) loaded.</summary>
    public static class BankTransactionMapping
    {
        public static TransactionModel ToModel(BankTransaction t) => new()
        {
            Id = t.BankTransactionId,
            transaction_id = t.PlaidTransactionId,
            account_id = t.FinancialAccount.PlaidAccountId,
            date = t.Date.ToString("yyyy-MM-dd"),
            authorized_date = t.AuthorizedDate?.ToString("yyyy-MM-dd"),
            name = t.Name,
            merchant_name = t.MerchantName,
            amount = (double)t.Amount,
            pending = t.Pending,
            payment_channel = t.PaymentChannel,
            transaction_type = t.TransactionType,
            Account = ToAccountModel(t.FinancialAccount),
            Category = t.Category == null ? null : new FinancialCategoryModel
            {
                FinancialCategoryId = t.Category.FinancialCategoryId,
                Name = t.Category.Name,
                SystemKey = t.Category.SystemKey,
                Icon = t.Category.Icon,
                Group = t.Category.Group,
                IsArchived = t.Category.IsArchived
            },
            Treatment = t.Treatment,
            TreatmentSource = t.TreatmentSource,
            CategorySource = t.CategorySource,
            Notes = t.Notes,
            Splits = (t.Splits ?? []).Select(s => new TransactionSplitModel
            {
                CategoryId = s.CategoryId,
                CategoryName = s.Category?.Name,
                Amount = s.Amount,
                Notes = s.Notes
            }).ToList(),
            AccountRole = t.FinancialAccount.BudgetRole,
            PlaidCategory = t.PfcDetailed,
            MerchantKey = t.MerchantKey
        };

        public static AccountModel ToAccountModel(FinancialAccount a) => new()
        {
            account_id = a.PlaidAccountId,
            name = a.Name,
            official_name = a.OfficialName,
            mask = a.Mask,
            type = a.Type,
            subtype = a.Subtype,
            balances = new BalancesModel
            {
                current = a.CurrentBalance,
                available = a.AvailableBalance,
                limit = a.CreditLimit,
                iso_currency_code = a.IsoCurrencyCode
            }
        };
    }
}
