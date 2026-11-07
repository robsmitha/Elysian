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

    /// <summary>
    /// The older budget page. Since Phase 2 it reads each transaction's own category and treatment: only spending and
    /// refunds on accounts that count as spending are counted; card payments, transfers, income and hidden transactions
    /// are listed as exemptions. A split transaction counts toward each of its categories.
    /// </summary>
    public class BudgetViewModel
    {
        public const int UncategorizedId = -1;
        public const int SplitId = -2;

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
        public string? ExpiredMessage => ExpiredAccessItems?.FirstOrDefault()?.Message;
        public decimal TransactionsTotal => (decimal)(Transactions?.Sum(t => t.amount) ?? 0);
        public decimal EstimateTotal => BudgetCategoryData.Where(c => c.FinancialCategoryId != UncategorizedId).Sum(c => c.Estimate);

        public int TotalPercent => EstimateTotal > 0 ? (int)Math.Round((TransactionsTotal / EstimateTotal) * 100) : 0;

        public BudgetViewModel(string name, DateTime startDate, DateTime endDate,
            List<BudgetCategoryModel> budgetCategories,
            List<TransactionModel> allTransactions,
            List<InstitutionAccessItemModel> budgetAccessItems,
            List<ExpiredAccessItem> expiredAccessItems)
        {
            BudgetName = name;
            StartDate = startDate;
            EndDate = endDate;
            BudgetAccessItems = budgetAccessItems;
            ExpiredAccessItems = expiredAccessItems;

            Transactions = allTransactions.Where(t => t.IsCounted).ToList();
            ExcludedTransactions = allTransactions.Where(t => !t.IsCounted).ToList();

            // The category column: the category, "Split", or "Uncategorized"
            foreach (var t in Transactions)
            {
                if (t.Splits.Count > 0)
                {
                    t.Category = new FinancialCategoryModel { FinancialCategoryId = SplitId, Name = $"Split · {t.Splits.Count} categories" };
                }
                else
                {
                    t.Category ??= new FinancialCategoryModel { FinancialCategoryId = UncategorizedId, Name = "Uncategorized" };
                }
            }

            // What each category got, with splits spread across their categories
            var lines = Transactions.SelectMany(t => t.Splits.Count > 0
                    ? t.Splits.Select(s => (Id: s.CategoryId ?? UncategorizedId, Name: s.CategoryName ?? "Uncategorized", s.Amount))
                    : [(Id: t.Category.FinancialCategoryId, Name: t.Category.Name, Amount: (decimal)t.amount)])
                .ToList();

            var rows = new List<BudgetCategoryModel>
            {
                new()
                {
                    FinancialCategoryId = UncategorizedId,
                    FinancialCategoryName = "Uncategorized",
                    Estimate = lines.Where(l => l.Id == UncategorizedId).Sum(l => l.Amount)
                }
            };
            rows.AddRange(budgetCategories);
            // Spending in a category the budget doesn't list still shows, with no estimate
            rows.AddRange(lines
                .Where(l => l.Id != UncategorizedId && rows.All(r => r.FinancialCategoryId != l.Id))
                .GroupBy(l => l.Id)
                .Select(g => new BudgetCategoryModel { FinancialCategoryId = g.Key, FinancialCategoryName = g.First().Name, Estimate = 0 }));

            BudgetCategories = rows.Select(c => new FinancialCategoryModel
            {
                FinancialCategoryId = c.FinancialCategoryId,
                Name = c.FinancialCategoryName
            }).ToList();

            BudgetCategoryData = rows.Select(c =>
            {
                var mine = lines.Where(l => l.Id == c.FinancialCategoryId).ToList();
                return new TransactionCategoryData
                {
                    FinancialCategoryId = c.FinancialCategoryId,
                    Category = c.FinancialCategoryName,
                    Estimate = c.Estimate,
                    Sum = mine.Sum(l => l.Amount),
                    Count = mine.Count
                };
            }).ToList();
        }

        public string DateRange => $"{StartDate:MMMM} {StartDate:MM/dd/yyyy} - {EndDate:MM/dd/yyyy}";
    }
}
