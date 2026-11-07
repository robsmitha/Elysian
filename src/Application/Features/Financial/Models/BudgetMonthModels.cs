using System.Text.Json.Serialization;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PaceStatus
    {
        /// <summary>Spent less than an even pace would have by now (or less than the target, for bills)</summary>
        OnTrack,
        /// <summary>Flexible spending more than 10% of the target ahead of an even pace</summary>
        OverPace,
        OverBudget,
        /// <summary>A fixed bill that has been paid in full</summary>
        Paid,
        /// <summary>Spending in a category with no target this month</summary>
        Unbudgeted
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PaycheckStatus
    {
        Received,
        Expected,
        /// <summary>The payday has passed by a few days and no deposit was seen; not counted</summary>
        Late
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecurringCadence
    {
        Weekly,
        Biweekly,
        Monthly,
        Quarterly,
        Yearly
    }

    public record PaycheckModel(DateOnly Payday, DateOnly? ReceivedOn, string Name, decimal Amount, PaycheckStatus Status, string? AccountName, int? IncomeStreamId);

    public record IncomeModel(
        decimal Total,
        decimal Received,
        decimal Expected,
        decimal? Override,
        List<PaycheckModel> Paychecks,
        /// <summary>Scheduled paydays in the month across all paychecks</summary>
        int Paydays,
        /// <summary>The next month with an extra paycheck (3 for biweekly, 5 for weekly), as yyyy-MM</summary>
        string? NextExtraPaycheckMonth,
        int Streams);

    public record UpcomingChargeModel(DateOnly Date, string Name, decimal Amount, bool IsEstimate, int? CategoryId, string? CategoryName, bool Counted,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] Treatment Treatment);

    public record BudgetLineModel(
        int CategoryId,
        string Name,
        string? Icon,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] CategoryGroup Group,
        /// <summary>The target set for this month</summary>
        decimal Target,
        /// <summary>Left over (or overspent, negative) from last month when rollover is on</summary>
        decimal Carry,
        /// <summary>Target plus carry</summary>
        decimal Available,
        bool Rollover,
        decimal Spent,
        decimal Pending,
        decimal Left,
        /// <summary>Where an even pace would be by today; flexible categories only</summary>
        decimal? ExpectedByNow,
        PaceStatus Status,
        /// <summary>How far over the target the month will end, counting recurring charges still to come</summary>
        decimal? ProjectedOver,
        UpcomingChargeModel? NextCharge);

    public record BudgetGroupModel([property: JsonConverter(typeof(JsonStringEnumConverter))] CategoryGroup Group, decimal Spent, decimal Available);

    public record UncategorizedModel(int Count, decimal Amount, string? Example, decimal? ExampleAmount);

    public record NotCountedModel(int CardPayments, decimal CardPaymentsTotal, int Transfers, decimal TransfersTotal, int Refunds, decimal RefundsTotal, int Hidden);

    public record BudgetAlertModel(string Kind, string Severity, int? CategoryId, string Message);

    public record BudgetMonthModel(
        int Year,
        int Month,
        bool IsCurrent,
        int DayOfMonth,
        int DaysInMonth,
        int DaysLeft,
        bool HasBudget,
        IncomeModel Income,
        decimal CommittedDebt,
        decimal CommittedGoals,
        decimal Committed,
        decimal Spent,
        decimal PendingSpent,
        decimal Assigned,
        decimal LeftInBudget,
        decimal SafeToSpend,
        decimal? SafePerDay,
        decimal Unassigned,
        List<BudgetLineModel> Lines,
        List<BudgetGroupModel> Groups,
        UncategorizedModel Uncategorized,
        NotCountedModel NotCounted,
        List<UpcomingChargeModel> Upcoming,
        List<BudgetAlertModel> Alerts);

    public record BudgetTargetSave(int CategoryId, decimal Amount, bool Rollover);

    public record RecurringModel(
        string MerchantKey,
        string Name,
        RecurringCadence Cadence,
        decimal Amount,
        bool IsVariable,
        /// <summary>The change from the previous charge when a steady price moved</summary>
        decimal? PriceChange,
        DateOnly LastDate,
        DateOnly NextDate,
        int? CategoryId,
        string? CategoryName,
        bool Counted,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] Treatment Treatment,
        int Occurrences);

    public record TrendCategoryModel(int CategoryId, string Name, decimal Amount);

    public record SpendingTrendModel(int Year, int Month, decimal Total, bool IsPartial, List<TrendCategoryModel> Categories);

    public class IncomeStreamModel
    {
        public int IncomeStreamId { get; set; }
        public string Name { get; set; } = "";
        public string MerchantKey { get; set; } = "";
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PayFrequency Frequency { get; set; }
        public DateOnly AnchorDate { get; set; }
        public decimal ExpectedAmount { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>The next payday on or after today</summary>
        public DateOnly? NextPayday { get; set; }
        /// <summary>For detected proposals: how many deposits the guess is based on</summary>
        public int? Deposits { get; set; }
    }

    public record IncomeStreamsModel(List<IncomeStreamModel> Streams, List<IncomeStreamModel> Proposals);

    public record NotificationSettingsModel(bool EmailEnabled, bool BankAlerts, bool BudgetAlerts, bool GoalAlerts);
}
