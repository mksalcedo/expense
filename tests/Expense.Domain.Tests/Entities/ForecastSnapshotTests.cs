using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class ForecastSnapshotTests : DatabaseTestBase
{
    [Fact]
    public async Task Snapshot_SavedWithLines_RoundTripsInOrder()
    {
        var snapshot = new ForecastSnapshot
        {
            AsOfDate = new DateOnly(2026, 7, 23),
            StartingBalance = 5193.58m,
            LowestProjectedBalance = 140.23m,
            LowestProjectedBalanceDate = new DateOnly(2026, 8, 5),
            CapturedAt = DateTimeOffset.UtcNow,
            Lines =
            [
                new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 25), Description = "Amex Payment", Amount = -4852.27m, RunningBalance = 3048.45m, AccountId = 2 },
                new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 5), Description = "GPC", Amount = -432m, RunningBalance = 140.23m, AccountId = 1 }
            ]
        };
        Context.ForecastSnapshots.Add(snapshot);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.ForecastSnapshots
            .Include(s => s.Lines)
            .SingleAsync(s => s.Id == snapshot.Id);

        Assert.Equal(new DateOnly(2026, 7, 23), reloaded.AsOfDate);
        Assert.Equal(140.23m, reloaded.LowestProjectedBalance);
        Assert.Equal(2, reloaded.Lines.Count);
        Assert.Contains(reloaded.Lines, l => l.Description == "Amex Payment" && l.Amount == -4852.27m);
    }

    [Fact]
    public async Task DeletingTheSnapshot_CascadesToDeleteItsLines()
    {
        var snapshot = new ForecastSnapshot
        {
            AsOfDate = new DateOnly(2026, 7, 22),
            StartingBalance = 1000m,
            LowestProjectedBalance = 500m,
            CapturedAt = DateTimeOffset.UtcNow,
            Lines = [new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 25), Description = "Test", Amount = -100m, RunningBalance = 900m, AccountId = 1 }]
        };
        Context.ForecastSnapshots.Add(snapshot);
        await Context.SaveChangesAsync();

        Context.ForecastSnapshots.Remove(snapshot);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        Assert.Equal(0, await reloadContext.ForecastSnapshotLines.CountAsync());
    }

    [Fact]
    public async Task AsOfDate_IsNotUnique_MultipleCapturesTheSameDayAreAllKept()
    {
        // AsOfDate used to be unique (one snapshot per calendar day) - changed because
        // upserting by day silently destroyed every earlier same-day capture, making any
        // intraday swing invisible (found live 2026-07-29, see
        // docs/forecast-history-redesign-plan.md). A capture happens after every sync now,
        // not once daily, so the same day legitimately has several real rows.
        Context.ForecastSnapshots.Add(new ForecastSnapshot
        {
            AsOfDate = new DateOnly(2026, 7, 23), StartingBalance = 1000m, LowestProjectedBalance = 500m, CapturedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        Context.ForecastSnapshots.Add(new ForecastSnapshot
        {
            AsOfDate = new DateOnly(2026, 7, 23), StartingBalance = 2000m, LowestProjectedBalance = 600m, CapturedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var count = await reloadContext.ForecastSnapshots.CountAsync(s => s.AsOfDate == new DateOnly(2026, 7, 23));
        Assert.Equal(2, count);
    }
}
