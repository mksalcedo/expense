using System.Linq.Expressions;
using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion.ManualCharges;

namespace Expense.Domain.Services.Ingestion;

/// <summary>
/// Single source of truth for "does this transaction count as real money that's already
/// moved, even though it hasn't formally posted yet." A still-pending Plaid transaction or a
/// self-reported Amex screenshot charge already represents real money - treating it as
/// invisible until PostedDate shows up creates a silent gap (a categorized charge missing
/// from this week's spending) or, in the opposite direction, a double-count (a starting
/// balance that already reflects Plaid's pending-inclusive "available" balance, plus a
/// separate forward-looking forecast line for the same not-yet-posted amount).
///
/// This exact check has had to be re-added after being missed more than once - see 55fab6b
/// (Spending Tracker/Amex forecast/Forecast Accuracy all missed Plaid pending charges) and
/// the partial-payment auto-reconciliation gap found live 2026-08-10 (matched only against
/// PostedDate != null, so three already-recorded, already-in-the-starting-balance Zelle/Venmo
/// transfers kept showing as live, doubling their amount into the projected balance). Any new
/// code that decides whether a BankTransaction "counts" - for spending, for forecasting, for
/// reconciliation - must use this rather than checking PostedDate directly.
/// </summary>
public static class BankTransactionReconciliation
{
    public static readonly Expression<Func<BankTransaction, bool>> CountsAsReal = t =>
        t.PostedDate != null
        || t.ImportSource == ManualChargeMatchingService.ManualScreenshotImportSource
        || t.ImportSource == "Plaid";

    /// <summary>
    /// The date to treat a transaction as having happened on - its real posted date once
    /// known, otherwise its transaction date (all that's available while still pending).
    /// </summary>
    public static DateOnly EffectiveDate(this BankTransaction transaction) =>
        transaction.PostedDate ?? transaction.TransactionDate;
}
