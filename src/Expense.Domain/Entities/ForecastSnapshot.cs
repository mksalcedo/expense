namespace Expense.Domain.Entities;

/// <summary>
/// A daily point-in-time capture of the forecast's key figures, so a future day's swing in
/// the lowest projected balance can be explained by diffing against yesterday's snapshot
/// instead of trying to reconstruct past forecast state after the fact (which the live,
/// never-persisted ForecastEngine can't do - it always computes "as of today"). One row
/// per calendar day, upserted as the day's data refreshes (see SyncScheduler).
/// </summary>
public class ForecastSnapshot
{
    public int Id { get; set; }
    public DateOnly AsOfDate { get; set; }
    public decimal StartingBalance { get; set; }
    public decimal LowestProjectedBalance { get; set; }
    public DateOnly? LowestProjectedBalanceDate { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public List<ForecastSnapshotLine> Lines { get; set; } = [];
}
