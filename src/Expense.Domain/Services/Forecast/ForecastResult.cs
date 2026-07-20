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
