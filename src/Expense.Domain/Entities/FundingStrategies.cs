namespace Expense.Domain.Entities;

/// <summary>
/// Plain string constants, not an enum, per the design doc's own wording for
/// funding_rules.strategy. Shared here so the Amex forecast query and any seed
/// data can't drift apart via a typo.
/// </summary>
public static class FundingStrategies
{
    /// <summary>
    /// Many real transactions in this category, each contributing to one recurring period
    /// budget (weekly/monthly/etc.) - tracked on Spending Tracker and reconciled
    /// cumulatively (see ForecastEngine's PartialPaymentCandidates path), never by a single
    /// "this one transaction is the whole story" match. Funded from whichever account
    /// budget_period.account_id names - a statement-cycle account (e.g. a pay-in-full
    /// credit card) pools every such category into one shared payment-due line; any other
    /// account gets its own standalone per-category line instead, since there's no
    /// separate "pay the bill later" step for money that already left the account.
    /// </summary>
    public const string TrackedBudget = "tracked_budget";
    public const string None = "none";
    public const string Direct = "direct";

    /// <summary>
    /// This category's expected amount is entered on its linked Account (MinPayment +
    /// ExtraPayment), not here - the debt-payment categories (Discover Payment, etc.).
    /// </summary>
    public const string AccountPayment = "account_payment";
}
