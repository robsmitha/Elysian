namespace Elysian.Application.Features.Financial.Models
{
    public class SaveCategoryModel : FinancialCategoryModel
    {
        public int BudgetId { get; set; }
        public decimal? Estimate { get; set; }
    }
}
