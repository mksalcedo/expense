namespace Expense.Domain.Entities;

/// <summary>Why one specific forecasted occurrence was manually excluded - see PaymentConfirmation.</summary>
public enum ConfirmationReason
{
    /// <summary>It genuinely already happened as scheduled, but automatic reconciliation can't verify it (see ForecastEngine).</summary>
    AlreadyPaid,

    /// <summary>The user is intentionally replacing this projected occurrence with their own plan (e.g. a split payment via One-Time Events) - no claim is made about whether/how it was paid.</summary>
    Overridden
}
