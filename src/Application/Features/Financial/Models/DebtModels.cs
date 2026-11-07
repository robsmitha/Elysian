using System.Text.Json.Serialization;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Models
{
    public class DebtModel
    {
        public int DebtId { get; set; }
        public string Name { get; set; } = "";
        /// <summary>"AMEX ••2210", or "entered by you"</summary>
        public string? Detail { get; set; }
        public int? FinancialAccountId { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DebtSource Source { get; set; } = DebtSource.Manual;
        public decimal Balance { get; set; }
        public DateOnly? BalanceAsOf { get; set; }
        /// <summary>The bank needs attention, so the balance may be out of date</summary>
        public bool IsStale { get; set; }
        public decimal Apr { get; set; }
        public decimal MinimumPayment { get; set; }
        public int? DueDay { get; set; }
        public decimal? PromoApr { get; set; }
        public DateOnly? PromoEndsOn { get; set; }
        public bool IsDeferredInterest { get; set; }
        public bool DetailsConfirmed { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>When the chosen strategy pays it off</summary>
        public DateOnly? PaidOffMonth { get; set; }
    }

    public record DebtLineModel(int DebtId, decimal Start, decimal Interest, decimal Payment, decimal End);

    /// <summary>One month of the schedule: balances at the start, interest added, payments, balances after</summary>
    public record DebtMonthModel(DateOnly Month, decimal Start, decimal Interest, decimal Paid, decimal End, List<DebtLineModel> Debts);

    public record PayoffSummaryModel(
        [property: JsonConverter(typeof(JsonStringEnumConverter))] DebtStrategy Strategy,
        decimal MonthlyAmount,
        int Months,
        /// <summary>The month of the last payment</summary>
        DateOnly? DebtFreeMonth,
        decimal TotalInterest,
        bool NeverPaysOff);

    public record PayoffModel(PayoffSummaryModel Summary, Dictionary<int, DateOnly?> PaidOff, List<DebtMonthModel> Schedule, List<string> Warnings);

    public record DebtPlanSettingsModel(
        decimal MonthlyAmount,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] DebtStrategy Strategy,
        List<int> CustomOrder,
        DateOnly StartDate);

    /// <summary>Planned vs. actual balance at the start of a month (D8), plus card payments found that month</summary>
    public record PlanVsActualModel(
        DateOnly Month,
        decimal Planned,
        decimal? Actual,
        decimal PaymentsFound,
        /// <summary>"Baseline", "OnTrack", "Ahead", "Behind"</summary>
        string Status,
        /// <summary>Planned − actual: positive is ahead</summary>
        decimal Difference,
        /// <summary>How much less than the plan's monthly amount was paid (finished months only)</summary>
        decimal? PaymentShortfall);

    public record DebtPlanModel(
        List<DebtModel> Debts,
        DebtPlanSettingsModel? Plan,
        /// <summary>The plan's amount, or the sum of minimums before a plan is saved</summary>
        decimal MonthlyAmount,
        decimal TotalBalance,
        decimal TotalMinimums,
        List<PayoffSummaryModel> Strategies,
        PayoffModel Selected,
        /// <summary>The selected strategy with the extra amount added, when one was asked for</summary>
        PayoffSummaryModel? WhatIf,
        List<PlanVsActualModel> History);

    public record SaveDebtPlanModel(decimal MonthlyAmount,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] DebtStrategy Strategy,
        List<int>? CustomOrder);
}
