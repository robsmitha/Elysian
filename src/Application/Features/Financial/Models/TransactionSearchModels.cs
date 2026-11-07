using System.Text.Json.Serialization;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Models
{
    /// <summary>The Transactions page's filters</summary>
    public enum TransactionView
    {
        All,
        /// <summary>Counted in budgets</summary>
        Spending,
        /// <summary>Card payments, transfers, income and hidden ones</summary>
        NotCounted,
        /// <summary>Counted but with no category yet</summary>
        Review,
        Pending
    }

    public record TransactionSearch(
        DateOnly? From = null,
        DateOnly? To = null,
        string? Query = null,
        int? FinancialAccountId = null,
        /// <summary>A category id; 0 means "no category"</summary>
        int? CategoryId = null,
        TransactionView View = TransactionView.All,
        int Page = 1,
        int PageSize = 50);

    public record TransactionViewCounts(int All, int Spending, int NotCounted, int Review, int Pending);

    public record TransactionSearchResult(List<TransactionModel> Items, int Total, TransactionViewCounts Counts, decimal CountedTotal);

    public record SplitInput(int? CategoryId, decimal Amount, string? Notes);

    /// <summary>"Always do this for …" from the detail panel</summary>
    public record RuleFromTransaction([property: JsonConverter(typeof(JsonStringEnumConverter))] RuleField Field, string Value, bool ApplyToPast);

    /// <summary>
    /// A change to one transaction. Unset fields are left alone; <see cref="ClearCategory"/> removes the category and
    /// <see cref="ResetTreatment"/> hands the treatment back to the automatic rules.
    /// </summary>
    public record TransactionUpdate(
        long Id,
        int? CategoryId = null,
        bool ClearCategory = false,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] Treatment? Treatment = null,
        bool ResetTreatment = false,
        /// <summary>Hands the category back to the automatic rules</summary>
        bool ResetCategory = false,
        string? Notes = null,
        /// <summary>Replaces the splits; an empty list removes them</summary>
        List<SplitInput>? Splits = null,
        RuleFromTransaction? CreateRule = null);

    public class CategoryRuleModel
    {
        public int CategoryRuleId { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RuleField Field { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RuleMatch Match { get; set; }
        public string Value { get; set; } = "";
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RuleDirection Direction { get; set; }
        public int? SetCategoryId { get; set; }
        public string? SetCategoryName { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Treatment? SetTreatment { get; set; }
        public int Priority { get; set; }
    }

    public record RuleSaveResult(CategoryRuleModel Rule, int Matches);
}
