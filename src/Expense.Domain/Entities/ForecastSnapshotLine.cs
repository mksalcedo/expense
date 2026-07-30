namespace Expense.Domain.Entities;

/// <summary>
/// One ledger row within a ForecastSnapshot's near-term window (see
/// ForecastSnapshotService for the exact horizon) - only the near future is persisted,
/// since the far tail of a 12-month forecast is both less actionable and would bloat
/// storage for no real diffing benefit.
/// </summary>
public class ForecastSnapshotLine
{
    public int Id { get; set; }
    public int ForecastSnapshotId { get; set; }
    public ForecastSnapshot ForecastSnapshot { get; set; } = null!;
    public DateOnly Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
    public int AccountId { get; set; }

    /// <summary>Propagated from ForecastLedgerRow.CategoryId - null for one-time events.
    /// Lets a later diff check whether a line missing from a newer snapshot was
    /// genuinely removed or resolved by a real reconciled transaction (see
    /// ForecastSnapshotDiffer and docs/forecast-history-redesign-plan.md).</summary>
    public int? CategoryId { get; set; }
}
