using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

public class ForecastSnapshotDiff
{
    public List<ForecastLineChange> Changed { get; set; } = [];
    public List<ForecastSnapshotLine> Added { get; set; } = [];
    public List<ForecastSnapshotLine> Removed { get; set; } = [];
}

public class ForecastLineChange
{
    public required string Description { get; set; }
    public int AccountId { get; set; }
    public DateOnly OldDate { get; set; }
    public DateOnly NewDate { get; set; }
    public decimal OldAmount { get; set; }
    public decimal NewAmount { get; set; }
}
