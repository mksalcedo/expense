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
        // CapturedAt (with Id as a final tiebreak for a true same-instant tie), not just
        // AsOfDate - there can be several real captures for the same calendar day now that
        // a capture happens after every sync, not just once daily.
        return await context.ForecastSnapshots
            .Where(s => s.AsOfDate >= cutoff)
            .OrderByDescending(s => s.CapturedAt).ThenByDescending(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<ForecastSnapshotDiff?> GetLatestDiffAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var mostRecentTwo = await context.ForecastSnapshots
            .OrderByDescending(s => s.CapturedAt).ThenByDescending(s => s.Id)
            .Take(2)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (mostRecentTwo.Count < 2) return null;

        return await GetDiffAsync(mostRecentTwo[1], mostRecentTwo[0], cancellationToken);
    }

    public async Task<ForecastSnapshotDiff?> GetDiffAsync(int olderSnapshotId, int newerSnapshotId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var older = await context.ForecastSnapshots.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == olderSnapshotId, cancellationToken);
        var newer = await context.ForecastSnapshots.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == newerSnapshotId, cancellationToken);
        if (older is null || newer is null) return null;

        var reconciledTransactions = await context.BankTransactions
            .Where(t => t.CategoryId != null && t.ReconciledOccurrenceDate != null)
            .ToListAsync(cancellationToken);

        // Explains a StartingBalanceChange - see ForecastSnapshotDiffer.Diff. Only checking
        // accounts feed the starting balance at all, so only their transactions are relevant.
        var checkingTransactions = await context.BankTransactions
            .Where(t => t.Account.Type == AccountType.Checking)
            .ToListAsync(cancellationToken);

        return ForecastSnapshotDiffer.Diff(older, newer, reconciledTransactions, checkingTransactions);
    }
}
