using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

/// <summary>Thin abstraction over ForecastSnapshot/ForecastSnapshotDiffer so UI components can be tested against a fake result.</summary>
public interface IForecastHistoryPageProvider
{
    Task<List<ForecastSnapshot>> GetRecentSnapshotsAsync(int days = 30, CancellationToken cancellationToken = default);

    /// <summary>Diff between the two most recent snapshots - null if fewer than two exist yet.</summary>
    Task<ForecastSnapshotDiff?> GetLatestDiffAsync(CancellationToken cancellationToken = default);

    /// <summary>Diff between any two specific snapshots the user picks, older vs newer -
    /// null if either id doesn't exist.</summary>
    Task<ForecastSnapshotDiff?> GetDiffAsync(int olderSnapshotId, int newerSnapshotId, CancellationToken cancellationToken = default);
}
