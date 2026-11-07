namespace Elysian.Domain.Constants
{
    /// <summary>What a transaction counts as in budgets (docs/budget-plan.md §4.4)</summary>
    public enum Treatment
    {
        /// <summary>Counts as spending in its category</summary>
        Spending,
        /// <summary>Money in from a paycheck or other income</summary>
        Income,
        /// <summary>Money back from a merchant: nets against the category it came from</summary>
        Refund,
        /// <summary>Money moving between the person's own accounts</summary>
        Transfer,
        /// <summary>Paying a credit card: the purchases already counted on the card</summary>
        CardPayment,
        /// <summary>Left out of budgets by the person</summary>
        Hidden
    }

    /// <summary>Who decided a transaction's category or treatment. A person's choice is never overwritten.</summary>
    public enum ClassificationSource
    {
        Auto,
        Rule,
        User
    }

    public enum CategoryGroup
    {
        Fixed,
        Flexible,
        NonMonthly
    }

    /// <summary>What a rule looks at</summary>
    public enum RuleField
    {
        /// <summary>The merchant, normalized ("TRADER JOE S #552" = "trader joe s")</summary>
        Merchant,
        /// <summary>The bank's description text</summary>
        Description,
        /// <summary>Plaid's detailed category code, e.g. FOOD_AND_DRINK_COFFEE</summary>
        PlaidCategory
    }

    public enum RuleMatch
    {
        Equals,
        Contains
    }

    public enum RuleDirection
    {
        Any,
        /// <summary>Money out (positive amounts)</summary>
        Out,
        /// <summary>Money in (negative amounts)</summary>
        In
    }
}
