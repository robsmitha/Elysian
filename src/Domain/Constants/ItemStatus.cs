namespace Elysian.Domain.Constants
{
    /// <summary>The health of a bank connection, as shown on the accounts page</summary>
    public enum ItemStatus
    {
        Healthy,
        /// <summary>The bank wants the person to sign in again (Plaid Link update mode fixes it)</summary>
        LoginRequired,
        /// <summary>The bank's consent ends soon; renewing through update mode keeps it syncing</summary>
        PendingExpiration,
        /// <summary>Access was revoked at the bank or removed here; nothing syncs</summary>
        Disconnected,
        /// <summary>Plaid reported an error that isn't fixed by signing in</summary>
        Error
    }

    /// <summary>How an account's transactions count toward budgets (decision D2: checking counts as spending)</summary>
    public enum AccountBudgetRole
    {
        Spending,
        IncomeOnly,
        Savings,
        Ignore
    }

    /// <summary>What started a sync</summary>
    public enum SyncTrigger
    {
        Initial,
        Webhook,
        Manual,
        Nightly,
        Reconnect
    }
}
