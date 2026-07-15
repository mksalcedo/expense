namespace Expense.Domain.Entities;

/// <summary>
/// Plain string constants, not an enum, per the design doc's own wording for
/// funding_rules.strategy. Shared here so the Amex forecast query and any seed
/// data can't drift apart via a typo.
/// </summary>
public static class FundingStrategies
{
    public const string PayInFullAmex = "pay_in_full_amex";
    public const string None = "none";
    public const string Direct = "direct";

    /// <summary>
    /// This category's expected amount is entered on its linked Account (MinPayment +
    /// ExtraPayment), not here - the debt-payment categories (Discover Payment, etc.).
    /// </summary>
    public const string AccountPayment = "account_payment";
}
