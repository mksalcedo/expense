namespace Expense.Domain.Entities;

/// <summary>
/// A manual exclusion of one specific occurrence of an account's forecasted payment -
/// excludes it from the forecast the same way a matching real transaction would (see
/// ForecastEngine), for cases the automatic CategoryId-based reconciliation can't cover.
/// Reason distinguishes "this genuinely already happened, I just can't prove it
/// automatically" (AlreadyPaid) from "I'm intentionally replacing this line with my own
/// plan" (Overridden, e.g. a split payment modeled via separate One-Time Events) - the
/// mechanism is identical either way, but the two mean different things and shouldn't be
/// conflated (a false "already paid" claim would misrepresent history). Removing this row
/// makes the occurrence reappear.
/// </summary>
public class PaymentConfirmation
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public DateOnly OriginalDate { get; set; }

    /// <summary>
    /// The date this occurrence was actually showing under at the moment it was confirmed/
    /// overridden - the deferred-to date if it had been deferred, otherwise OriginalDate.
    /// Captured once and never recomputed, so the excluded row stays put even if the
    /// underlying deferral is later changed or removed.
    /// </summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>Captured at confirm/override time, same reason - a stable snapshot for display, not re-derived from the (possibly since-changed) account/category.</summary>
    public decimal Amount { get; set; }

    public ConfirmationReason Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
