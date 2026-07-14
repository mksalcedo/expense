using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class CheckingBalanceSnapshotTests : DatabaseTestBase
{
    [Fact]
    public async Task Snapshot_SavedAndReloaded_RoundTripsCorrectly()
    {
        var snapshot = new CheckingBalanceSnapshot
        {
            AsOfDate = new DateOnly(2026, 7, 14),
            Balance = 6463.02m
        };
        Context.CheckingBalanceSnapshots.Add(snapshot);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.CheckingBalanceSnapshots.SingleAsync(s => s.Id == snapshot.Id);

        Assert.Equal(new DateOnly(2026, 7, 14), reloaded.AsOfDate);
        Assert.Equal(6463.02m, reloaded.Balance);
    }

    [Fact]
    public async Task LatestSnapshot_IsTheOneWithTheMostRecentAsOfDate()
    {
        Context.CheckingBalanceSnapshots.AddRange(
            new CheckingBalanceSnapshot { AsOfDate = new DateOnly(2026, 7, 1), Balance = 5000m },
            new CheckingBalanceSnapshot { AsOfDate = new DateOnly(2026, 7, 14), Balance = 6463.02m },
            new CheckingBalanceSnapshot { AsOfDate = new DateOnly(2026, 7, 7), Balance = 5500m }
        );
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var latest = await reloadContext.CheckingBalanceSnapshots
            .OrderByDescending(s => s.AsOfDate)
            .FirstAsync();

        Assert.Equal(6463.02m, latest.Balance);
    }
}
