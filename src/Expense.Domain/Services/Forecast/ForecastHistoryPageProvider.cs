using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all diff logic lives in ForecastSnapshotDiffer.</summary>
public class ForecastHistoryPageProvider(IDbContextFactory<ExpenseDbContext> contextFactory) : IForecastHistoryPageProvider
{
    public async Task<List<ForecastSnapshot>> GetRecentSnapshotsAsync(int days = 30, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateOnly.FromDateTime(DateTime.Today).AddDays(-days);
        return await context.ForecastSnapshots
            .Where(s => s.AsOfDate >= cutoff)
            .OrderByDescending(s => s.AsOfDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<ForecastSnapshotDiff?> GetLatestDiffAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var mostRecentTwo = await context.ForecastSnapshots
            .Include(s => s.Lines)
            .OrderByDescending(s => s.AsOfDate)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (mostRecentTwo.Count < 2) return null;

        var current = mostRecentTwo[0];
        var previous = mostRecentTwo[1];
        return ForecastSnapshotDiffer.Diff(previous, current);
    }
}
