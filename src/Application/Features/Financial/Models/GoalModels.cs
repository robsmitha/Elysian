using System.Text.Json.Serialization;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GoalStatus
    {
        OnTrack,
        Ahead,
        Behind,
        /// <summary>Saved at least the target</summary>
        Funded,
        /// <summary>A one-time goal past its due date without the full amount</summary>
        Overdue
    }

    public record GoalContributionModel(int GoalContributionId, DateOnly Date, decimal Amount,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] ContributionSource Source, string? Note, long? BankTransactionId);

    public record PastCycleModel(DateOnly Start, DateOnly Due, decimal Target, decimal Saved);

    public class GoalModel
    {
        public int SavingsGoalId { get; set; }
        public string Name { get; set; } = "";
        public string? Icon { get; set; }
        public decimal TargetAmount { get; set; }
        public DateOnly DueDate { get; set; }
        public DateOnly CycleStart { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GoalCycle Cycle { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GoalCycleEnd CycleEnd { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GoalFundingMode FundingMode { get; set; }
        public int? LinkedAccountId { get; set; }
        public string? LinkedAccountName { get; set; }
        public decimal StartingBalance { get; set; }
        public bool IsArchived { get; set; }

        // Worked out (read-only)
        public decimal Saved { get; set; }
        public decimal SavedAtMonthStart { get; set; }
        public decimal ExpectedByNow { get; set; }
        /// <summary>To set aside each month: what was still needed at the start of this month ÷ months left</summary>
        public decimal RequiredMonthly { get; set; }
        public int MonthsLeft { get; set; }
        public decimal ContributedThisMonth { get; set; }
        public GoalStatus Status { get; set; }
        /// <summary>Saved − expected by now: positive is ahead</summary>
        public decimal Difference { get; set; }
        public List<GoalContributionModel> Contributions { get; set; } = [];
        public List<PastCycleModel> PastCycles { get; set; } = [];
    }

    public record GoalAccountModel(int FinancialAccountId, string Name, decimal Balance,
        /// <summary>Saved toward shared goals kept here</summary>
        decimal Allocated,
        /// <summary>Balance − allocated; negative means the goals claim more than is there</summary>
        decimal Unallocated);

    /// <summary>A recent transfer into a savings account that isn't counted toward a goal yet</summary>
    public record GoalTransferModel(long BankTransactionId, DateOnly Date, decimal Amount, string AccountName, string Name);

    public record GoalsModel(
        List<GoalModel> Goals,
        decimal NeededThisMonth,
        decimal TotalSaved,
        int OnTrack,
        List<GoalAccountModel> Accounts,
        List<GoalTransferModel> Transfers);

    public record AddContributionModel(int SavingsGoalId, decimal? Amount, DateOnly? Date, long? BankTransactionId, string? Note);
}
