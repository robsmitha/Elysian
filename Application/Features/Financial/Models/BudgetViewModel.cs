using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Models
{
    public class TransactionCategoryData
    {
        public int FinancialCategoryId { get; set; }
        public string Category { get; set; }
        public decimal Sum { get; set; } = 0;
        public decimal Estimate { get; set; } = 0;
        public int Count { get; set; }

        public int TotalPercent => Estimate > 0 ? (int)Math.Round((Sum / Estimate) * 100) : 0;
    }

    public class BudgetViewModel
    {
        public string BudgetName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<TransactionCategoryData> BudgetCategoryData { get; set; }
        public List<TransactionModel> Transactions { get; set; }
        public List<TransactionModel> ExcludedTransactions { get; set; }
        public List<FinancialCategoryModel> BudgetCategories { get; set; }
        public List<InstitutionAccessItemModel> BudgetAccessItems { get; set; }
        public List<ExpiredAccessItem> ExpiredAccessItems { get; set; }

        public bool HasExpiredAccessItems => ExpiredAccessItems?.Any() == true;
        public string ExpiredMessage => ExpiredAccessItems?.FirstOrDefault()?.Message;
        public decimal TransactionsTotal => (decimal)(Transactions?.Sum(t => t.amount) ?? 0);
        public decimal EstimateTotal => BudgetCategoryData.Where(c => c.FinancialCategoryId != -1)?.Sum(c => c.Estimate) ?? 0;

        public int TotalPercent => EstimateTotal > 0 ? (int)Math.Round((TransactionsTotal / EstimateTotal) * 100) : 0;

        public BudgetViewModel(string name, DateTime startDate, DateTime endDate,
            List<BudgetCategoryModel> budgetCategories,
            List<TransactionCategoryModel> transactionCategories,
            List<TransactionModel> allTransactions,
            List<InstitutionAccessItemModel> budgetAccessItems,
            List<ExpiredAccessItem> expiredAccessItems,
            List<BudgetExcludedTransactionModel> budgetExcludedTransactions)
        {
            BudgetName = name;
            StartDate = startDate;
            EndDate = endDate;
            BudgetAccessItems = budgetAccessItems;
            ExpiredAccessItems = expiredAccessItems;

            var excludedTransactionIds = budgetExcludedTransactions.Select(e => e.TransactionId).ToHashSet(); 
            
            Transactions = (from t in allTransactions
                            join c in transactionCategories on t.transaction_id equals c.TransactionId into tmpC
                            from c in tmpC.DefaultIfEmpty(new TransactionCategoryModel
                            {
                                FinancialCategoryId = -1,
                                FinancialCategoryName = "Uncategorized"
                            })
                            where !excludedTransactionIds.Contains(t.transaction_id)
                            select TransactionWithCategory(t, c)).ToList();

            ExcludedTransactions = allTransactions.Where(t => excludedTransactionIds.Contains(t.transaction_id)).ToList();

            budgetCategories.Insert(0, new BudgetCategoryModel
            {
                FinancialCategoryId = -1,
                FinancialCategoryName = "Uncategorized",
                Estimate = (decimal)Transactions.Where(t => t.Category.FinancialCategoryId == -1).Sum(t => t.amount)
            });

            BudgetCategories = budgetCategories.Select(c => new FinancialCategoryModel
            {
                FinancialCategoryId = c.FinancialCategoryId,
                Name = c.FinancialCategoryName
            }).ToList();

            BudgetCategoryData = (from c in budgetCategories
                                  join t in Transactions on c.FinancialCategoryId equals t.Category.FinancialCategoryId into tmpT
                                  from t in tmpT.DefaultIfEmpty(new TransactionModel
                                  {
                                      amount = 0
                                  })
                                  group t.amount by new { c.FinancialCategoryId, c.FinancialCategoryName, c.Estimate } into g
                                  select new TransactionCategoryData
                                  {
                                      FinancialCategoryId = g.Key.FinancialCategoryId,
                                      Estimate = g.Key.Estimate,
                                      Category = g.Key.FinancialCategoryName,
                                      Sum = (decimal)g.Sum(),
                                      Count = g.Count()
                                  }).ToList();
        }

        public string DateRange => $"{StartDate:MMMM} {StartDate:MM/dd/yyyy} - {EndDate:MM/dd/yyyy}";

        static TransactionModel TransactionWithCategory(TransactionModel t, TransactionCategoryModel c)
        {
            t.Category = new FinancialCategoryModel
            {
                FinancialCategoryId = c.FinancialCategoryId,
                Name = c.FinancialCategoryName
            };
            return t;
        }
    }
}
