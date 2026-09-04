using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

public class ForecastLedgerRow
{
    public required DateOnly Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public required decimal RunningBalance { get; set; }
    public int AccountId { get; set; }
    public DateOnly OriginalDate { get; set; }
    public bool IsDeferred { get; set; }
    public int? DeferralId { get; set; }

    /// <summary>True when a PaymentAmountAdjustment corrected this occurrence's projected
    /// Amount to a real known figure - unlike IsExcluded, the row stays fully live/
    /// actionable (not resolved/paid), just using a corrected estimate.</summary>
    public bool IsAmountAdjusted { get; set; }
    public int? AdjustmentId { get; set; }

    /// <summary>The normally-computed amount before the adjustment was applied - null unless IsAmountAdjusted.</summary>
    public decimal? OriginalScheduledAmount { get; set; }

    /// <summary>Propagated from LedgerLine.CategoryId (see there) - null for one-time
    /// events. Carried through so a captured ForecastSnapshotLine can later be checked
    /// against real reconciled transactions, to tell "genuinely removed" apart from
    /// "resolved by a real transaction for a somewhat different amount" when diffing.</summary>
    public int? CategoryId { get; set; }

    /// <summary>True for a manually confirmed/overridden occurrence - stays in place in the
    /// ledger (see ForecastEngine) rather than being removed, so its amount/date remain
    /// visible instead of only living in a separate undo list.</summary>
    public bool IsExcluded { get; set; }
    public ConfirmationReason? ExclusionReason { get; set; }
    public int? ConfirmationId { get; set; }

    /// <summary>
    /// When this row was actually resolved - null when there isn't one clean single answer
    /// (a partial-payment-matched row already spells its own date out in Description, so
    /// this is left unset there rather than showing the same date twice). Sourced per case:
    /// the real transaction's own date for a single-transaction AutoReconciled match,
    /// PaymentConfirmation.CreatedAt for a manual confirm/override, or the most recent
    /// applied partial payment's PaidDate once a multi-payer line is fully covered.
    /// </summary>
    public DateOnly? ResolvedDate { get; set; }

    /// <summary>Real partial payments already applied to this occurrence - their sum is
    /// already subtracted from Amount above (see ForecastEngine); kept here only so the
    /// Forecast page can list/undo each one individually.</summary>
    public List<PartialPaymentSummary> PartialPayments { get; set; } = [];

    /// <summary>
    /// A real transaction matching this line's category and date, but whose amount fell
    /// outside the auto-reconciliation tolerance (see ForecastEngine.ReconciliationAmountToleranceFraction)
    /// - close enough to plausibly be the same real-world bill at a different amount than
    /// budgeted, not close enough for automation to assume it safely. Surfaced so Override
    /// can pre-fill with it instead of leaving the user to go look the real number up
    /// themselves (found live 2026-08-04: a $70.97 real Gas bill against a $76.68 budgeted
    /// line never got flagged anywhere on its own).
    /// </summary>
    public decimal? SuggestedOverrideAmount { get; set; }

    /// <summary>The near-miss transaction's own real posted date (PostedDate ?? TransactionDate)
    /// - not necessarily this line's own date, since the transaction's ReconciledOccurrenceDate
    /// (which category+date matching uses) records which bill it was assigned to, not when it
    /// actually happened.</summary>
    public DateOnly? SuggestedOverrideDate { get; set; }

    /// <summary>
    /// Null for a normal category - use SuggestedOverrideAmount/Date instead (a single real
    /// transaction is assumed to be the whole story). Non-null (possibly empty) for a category
    /// with ReconcileByCalendarMonth set, where several real transactions legitimately
    /// contribute to one line (e.g. Piano - several payers on their own schedules) and no
    /// single one should ever be offered as "the" resolution via Change Amount. Every real
    /// transaction reconciled to this line's date, whether already recorded as a partial
    /// payment (PartialPaymentId set, for Undo) or still unclaimed (PartialPaymentId null, for
    /// a one-click "Record partial income") - plus any recorded partial payment that never
    /// matched a real transaction at all (manually entered, not yet synced or never will be),
    /// so nothing recorded here is ever silently lost from view.
    /// </summary>
    public List<PartialPaymentCandidate>? PartialPaymentCandidates { get; set; }

    /// <summary>
    /// True for a TrackedBudget category's standalone per-period line (see ForecastEngine) -
    /// its Amount already recomputes itself automatically from real spending every render,
    /// and it resolves itself once its period passes, with no separate "payment" event to
    /// confirm. None of Defer/Confirm/Override/Adjust/Partial-pay apply - clicking any of
    /// them would just create a confirmation/deferral/adjustment record that duplicates or
    /// conflicts with this row instead of doing anything useful, so the Forecast page hides
    /// them entirely for a row like this.
    /// </summary>
    public bool IsTrackedBudgetLine { get; set; }
}

public class PartialPaymentSummary
{
    public required int PartialPaymentId { get; set; }
    public required decimal Amount { get; set; }
    public required DateOnly PaidDate { get; set; }
}

public class PartialPaymentCandidate
{
    public required decimal Amount { get; set; }
    public required DateOnly Date { get; set; }

    /// <summary>Null if this is a real transaction found nearby but not yet recorded as a
    /// partial payment; set (to the real PartialPaymentId, for Undo) once one already covers it.</summary>
    public int? PartialPaymentId { get; set; }
}

public class ForecastResult
{
    public required decimal StartingBalance { get; set; }
    public required List<ForecastLedgerRow> Rows { get; set; }

    public decimal LowestProjectedBalance =>
        Rows.Count == 0 ? StartingBalance : Rows.Min(r => r.RunningBalance);

    public DateOnly? LowestProjectedBalanceDate
    {
        get
        {
            if (Rows.Count == 0) return null;
            var lowest = Rows.Min(r => r.RunningBalance);
            return Rows.First(r => r.RunningBalance == lowest).Date;
        }
    }
}
