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
        public Task<List<ForecastSnapshot>> GetRecentSnapshotsAsync(int days = 30, CancellationToken cancellationToken = default) => Task.FromResult(snapshots);
        public Task<ForecastSnapshotDiff?> GetLatestDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult(diff);
    }

    private void RegisterFakes(List<ForecastSnapshot>? snapshots = null, ForecastSnapshotDiff? diff = null) =>
        Services.AddSingleton<IForecastHistoryPageProvider>(new FakeForecastHistoryPageProvider(snapshots ?? [], diff));

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
}
