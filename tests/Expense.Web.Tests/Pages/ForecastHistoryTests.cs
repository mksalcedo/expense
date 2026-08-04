using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class ForecastHistoryTests : BunitContext
{
    private class FakeForecastHistoryPageProvider(List<ForecastSnapshot> snapshots, ForecastSnapshotDiff? diff) : IForecastHistoryPageProvider
    {
        public ForecastSnapshotDiff? NextManualDiffResult { get; set; }
        public int? LastRequestedOlderSnapshotId { get; private set; }
        public int? LastRequestedNewerSnapshotId { get; private set; }

        public Task<List<ForecastSnapshot>> GetRecentSnapshotsAsync(int days = 30, CancellationToken cancellationToken = default) => Task.FromResult(snapshots);
        public Task<ForecastSnapshotDiff?> GetLatestDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult(diff);

        public Task<ForecastSnapshotDiff?> GetDiffAsync(int olderSnapshotId, int newerSnapshotId, CancellationToken cancellationToken = default)
        {
            LastRequestedOlderSnapshotId = olderSnapshotId;
            LastRequestedNewerSnapshotId = newerSnapshotId;
            return Task.FromResult(NextManualDiffResult);
        }
    }

    private FakeForecastHistoryPageProvider RegisterFakes(List<ForecastSnapshot>? snapshots = null, ForecastSnapshotDiff? diff = null)
    {
        var provider = new FakeForecastHistoryPageProvider(snapshots ?? [], diff);
        Services.AddSingleton<IForecastHistoryPageProvider>(provider);
        return provider;
    }

    [Fact]
    public void NoSnapshotsYet_ShowsFriendlyEmptyStates()
    {
        RegisterFakes();

        var cut = Render<ForecastHistory>();

        Assert.Contains("No snapshots captured yet", cut.Markup);
        Assert.Contains("Not enough snapshots yet", cut.Markup);
    }

    [Fact]
    public void RendersOneTrendRowPerSnapshot()
    {
        RegisterFakes(snapshots:
        [
            new ForecastSnapshot { AsOfDate = new DateOnly(2026, 7, 24), StartingBalance = 1000m, LowestProjectedBalance = 250m, LowestProjectedBalanceDate = new DateOnly(2026, 8, 1) },
            new ForecastSnapshot { AsOfDate = new DateOnly(2026, 7, 23), StartingBalance = 1000m, LowestProjectedBalance = 1100m, LowestProjectedBalanceDate = new DateOnly(2026, 8, 3) }
        ]);

        var cut = Render<ForecastHistory>();

        Assert.Contains("250.00", cut.Markup);
        Assert.Contains("1,100.00", cut.Markup);
        Assert.Equal(2, cut.FindAll("table").Last().QuerySelectorAll("tbody tr").Length);
    }

    [Fact]
    public void ChangedLine_ShowsOldAndNewDateAndAmount()
    {
        var diff = new ForecastSnapshotDiff
        {
            Changed =
            [
                new ForecastLineChange
                {
                    Description = "Piano", AccountId = 1,
                    OldDate = new DateOnly(2026, 7, 5), NewDate = new DateOnly(2026, 7, 5),
                    OldAmount = -600m, NewAmount = -25m
                }
            ]
        };
        RegisterFakes(diff: diff);

        var cut = Render<ForecastHistory>();

        Assert.Contains("Piano", cut.Markup);
        Assert.Contains("-600.00", cut.Markup);
        Assert.Contains("-25.00", cut.Markup);
    }

    [Fact]
    public void AddedAndRemovedLines_AreListedUnderTheirOwnHeadings()
    {
        var diff = new ForecastSnapshotDiff
        {
            Added = [new ForecastSnapshotLine { Description = "New Subscription", AccountId = 1, Date = new DateOnly(2026, 7, 30), Amount = -15m }],
            Removed = [new ForecastSnapshotLine { Description = "Cancelled Gym", AccountId = 1, Date = new DateOnly(2026, 7, 28), Amount = -40m }]
        };
        RegisterFakes(diff: diff);

        var cut = Render<ForecastHistory>();

        Assert.Contains("New Subscription", cut.Markup);
        Assert.Contains("Cancelled Gym", cut.Markup);
        Assert.Contains("Added", cut.Markup);
        Assert.Contains("Removed", cut.Markup);
    }

    [Fact]
    public void NoChangesSinceLastSnapshot_ShowsFriendlyMessage()
    {
        RegisterFakes(diff: new ForecastSnapshotDiff());

        var cut = Render<ForecastHistory>();

        Assert.Contains("Nothing changed since the last snapshot", cut.Markup);
    }

    [Fact]
    public void StartingBalanceChange_IsShownExplicitly_WithTheDelta()
    {
        var diff = new ForecastSnapshotDiff
        {
            StartingBalanceChange = new StartingBalanceChange { OldBalance = 4488.63m, NewBalance = 4418.31m }
        };
        RegisterFakes(diff: diff);

        var cut = Render<ForecastHistory>();

        Assert.Contains("4,488.63", cut.Markup);
        Assert.Contains("4,418.31", cut.Markup);
        Assert.Contains("-70.32", cut.Markup);
    }

    [Fact]
    public void StartingBalanceChange_WithTransactions_ListsThemWithARunningTotal()
    {
        var diff = new ForecastSnapshotDiff
        {
            StartingBalanceChange = new StartingBalanceChange
            {
                OldBalance = 4649.18m, NewBalance = 4209.21m,
                Transactions =
                [
                    new StartingBalanceTransaction { Date = new DateOnly(2026, 7, 31), Description = "GWINNETT CTY WATER", Amount = -378.20m },
                    new StartingBalanceTransaction { Date = new DateOnly(2026, 8, 2), Description = "ZELLE FROM GABRIEL NAVA", Amount = 95.00m }
                ]
            }
        };
        RegisterFakes(diff: diff);

        var cut = Render<ForecastHistory>();

        Assert.Contains("GWINNETT CTY WATER", cut.Markup);
        Assert.Contains("-378.20", cut.Markup);
        Assert.Contains("ZELLE FROM GABRIEL NAVA", cut.Markup);
        var total = cut.Find("#starting-balance-transactions tfoot").TextContent;
        Assert.Contains("-283.20", total);
    }

    [Fact]
    public void StartingBalanceChange_WithNoTransactions_ShowsNoBreakdownTable()
    {
        var diff = new ForecastSnapshotDiff
        {
            StartingBalanceChange = new StartingBalanceChange { OldBalance = 4488.63m, NewBalance = 4418.31m }
        };
        RegisterFakes(diff: diff);

        var cut = Render<ForecastHistory>();

        Assert.Empty(cut.FindAll("#starting-balance-transactions"));
    }

    [Fact]
    public void StartingBalanceChangeAlone_WithNoLineChanges_DoesNotShowTheNothingChangedMessage()
    {
        var diff = new ForecastSnapshotDiff
        {
            StartingBalanceChange = new StartingBalanceChange { OldBalance = 4488.63m, NewBalance = 4418.31m }
        };
        RegisterFakes(diff: diff);

        var cut = Render<ForecastHistory>();

        Assert.DoesNotContain("Nothing changed since the last snapshot", cut.Markup);
    }

    [Fact]
    public void ReconciledLine_ShowsBudgetedAndActualAmounts_AndTheVariance()
    {
        var diff = new ForecastSnapshotDiff
        {
            Reconciled =
            [
                new ReconciledLine { Description = "Groceries", AccountId = 1, Date = new DateOnly(2026, 7, 25), BudgetedAmount = -150m, ActualAmount = -162.37m }
            ]
        };
        RegisterFakes(diff: diff);

        var cut = Render<ForecastHistory>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("-150.00", cut.Markup);
        Assert.Contains("-162.37", cut.Markup);
        Assert.Contains("12.37", cut.Markup);
    }

    [Fact]
    public void ReconciledLineAlone_WithNoOtherChanges_DoesNotShowTheNothingChangedMessage()
    {
        var diff = new ForecastSnapshotDiff
        {
            Reconciled = [new ReconciledLine { Description = "Groceries", AccountId = 1, Date = new DateOnly(2026, 7, 25), BudgetedAmount = -150m, ActualAmount = -162.37m }]
        };
        RegisterFakes(diff: diff);

        var cut = Render<ForecastHistory>();

        Assert.DoesNotContain("Nothing changed since the last snapshot", cut.Markup);
    }

    [Fact]
    public void SnapshotPickers_DefaultToTheTwoMostRecentSnapshots()
    {
        RegisterFakes(snapshots:
        [
            new ForecastSnapshot { Id = 3, AsOfDate = new DateOnly(2026, 7, 24), CapturedAt = new DateTimeOffset(2026, 7, 24, 15, 0, 0, TimeSpan.Zero), StartingBalance = 1000m, LowestProjectedBalance = 250m },
            new ForecastSnapshot { Id = 2, AsOfDate = new DateOnly(2026, 7, 24), CapturedAt = new DateTimeOffset(2026, 7, 24, 6, 0, 0, TimeSpan.Zero), StartingBalance = 1000m, LowestProjectedBalance = 300m },
            new ForecastSnapshot { Id = 1, AsOfDate = new DateOnly(2026, 7, 23), CapturedAt = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero), StartingBalance = 1000m, LowestProjectedBalance = 1100m }
        ]);

        var cut = Render<ForecastHistory>();

        Assert.Equal("2", cut.Find("#compare-from-select").GetAttribute("value"));
        Assert.Equal("3", cut.Find("#compare-to-select").GetAttribute("value"));
    }

    [Fact]
    public void ClickingCompare_RequestsTheDiffForTheSelectedSnapshots()
    {
        var provider = RegisterFakes(snapshots:
        [
            new ForecastSnapshot { Id = 3, AsOfDate = new DateOnly(2026, 7, 24), CapturedAt = new DateTimeOffset(2026, 7, 24, 15, 0, 0, TimeSpan.Zero), StartingBalance = 1000m, LowestProjectedBalance = 250m },
            new ForecastSnapshot { Id = 2, AsOfDate = new DateOnly(2026, 7, 24), CapturedAt = new DateTimeOffset(2026, 7, 24, 6, 0, 0, TimeSpan.Zero), StartingBalance = 1000m, LowestProjectedBalance = 300m },
            new ForecastSnapshot { Id = 1, AsOfDate = new DateOnly(2026, 7, 23), CapturedAt = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero), StartingBalance = 1000m, LowestProjectedBalance = 1100m }
        ]);
        provider.NextManualDiffResult = new ForecastSnapshotDiff();
        var cut = Render<ForecastHistory>();

        cut.Find("#compare-from-select").Change("1");
        cut.Find("#compare-to-select").Change("2");
        cut.Find("#compare-btn").Click();

        Assert.Equal(1, provider.LastRequestedOlderSnapshotId);
        Assert.Equal(2, provider.LastRequestedNewerSnapshotId);
    }
}
