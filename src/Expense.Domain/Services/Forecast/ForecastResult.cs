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
}

public class PartialPaymentSummary
{
    public required int PartialPaymentId { get; set; }
    public required decimal Amount { get; set; }
    public required DateOnly PaidDate { get; set; }
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
