using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class DebtBalanceSnapshotTests : DatabaseTestBase
{
    [Fact]
    public async Task Snapshot_SavedAndReloaded_RoundTripsCorrectly()
    {
        var account = new Account { Name = "Discover", Type = AccountType.Debt };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();

        var snapshot = new DebtBalanceSnapshot
        {
            AccountId = account.Id,
            AsOfDate = new DateOnly(2026, 7, 14),
            Balance = 8568.13m
        };
        Context.DebtBalanceSnapshots.Add(snapshot);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.DebtBalanceSnapshots.SingleAsync(s => s.Id == snapshot.Id);

        Assert.Equal(account.Id, reloaded.AccountId);
        Assert.Equal(8568.13m, reloaded.Balance);
    }

    [Fact]
    public async Task Snapshots_SupportHistoryOverTime_ForTrendChart()
    {
        var account = new Account { Name = "Discover", Type = AccountType.Debt };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();

        Context.DebtBalanceSnapshots.AddRange(
            new DebtBalanceSnapshot { AccountId = account.Id, AsOfDate = new DateOnly(2026, 1, 1), Balance = 9000m },
            new DebtBalanceSnapshot { AccountId = account.Id, AsOfDate = new DateOnly(2026, 4, 1), Balance = 8700m },
            new DebtBalanceSnapshot { AccountId = account.Id, AsOfDate = new DateOnly(2026, 7, 1), Balance = 8568.13m }
        );
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var history = await reloadContext.DebtBalanceSnapshots
            .Where(s => s.AccountId == account.Id)
            .OrderBy(s => s.AsOfDate)
            .ToListAsync();

        Assert.Equal(3, history.Count);
        Assert.Equal(9000m, history[0].Balance);
        Assert.Equal(8568.13m, history[^1].Balance);
    }
}
