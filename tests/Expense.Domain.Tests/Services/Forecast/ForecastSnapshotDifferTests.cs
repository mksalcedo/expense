using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;

namespace Expense.Domain.Tests.Services.Forecast;

public class ForecastSnapshotDifferTests
{
    private static ForecastSnapshot MakeSnapshot(params ForecastSnapshotLine[] lines) => new()
    {
        AsOfDate = new DateOnly(2026, 7, 23), StartingBalance = 1000m, LowestProjectedBalance = 500m, Lines = lines.ToList()
    };

    [Fact]
    public void LineWithADifferentAmount_IsReportedAsChanged()
    {
        var previous = MakeSnapshot(new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 25), Description = "Amex Payment", Amount = -4852.27m, RunningBalance = 3048m, AccountId = 2 });
        var current = MakeSnapshot(new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 25), Description = "Amex Payment", Amount = -5852.27m, RunningBalance = 2048m, AccountId = 2 });

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        var change = Assert.Single(diff.Changed);
        Assert.Equal("Amex Payment", change.Description);
        Assert.Equal(-4852.27m, change.OldAmount);
        Assert.Equal(-5852.27m, change.NewAmount);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void LineWithADifferentDate_IsReportedAsChanged_NotRemovedAndAdded()
    {
        var previous = MakeSnapshot(new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 20), Description = "SoFi Payment", Amount = -1107.24m, RunningBalance = 3864m, AccountId = 12 });
        var current = MakeSnapshot(new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 22), Description = "SoFi Payment", Amount = -1107.24m, RunningBalance = 3864m, AccountId = 12 });

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        var change = Assert.Single(diff.Changed);
        Assert.Equal(new DateOnly(2026, 7, 20), change.OldDate);
        Assert.Equal(new DateOnly(2026, 7, 22), change.NewDate);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void LineOnlyInCurrent_IsReportedAsAdded()
    {
        var previous = MakeSnapshot();
        var current = MakeSnapshot(new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 30), Description = "HVAC repair", Amount = -850m, RunningBalance = 150m, AccountId = 1 });

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        var added = Assert.Single(diff.Added);
        Assert.Equal("HVAC repair", added.Description);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void LineOnlyInPrevious_IsReportedAsRemoved()
    {
        var previous = MakeSnapshot(new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 25), Description = "Amex Payment (partial)", Amount = -1000m, RunningBalance = 4048m, AccountId = 2 });
        var current = MakeSnapshot();

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        var removed = Assert.Single(diff.Removed);
        Assert.Equal("Amex Payment (partial)", removed.Description);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Added);
    }

    [Fact]
    public void IdenticalLines_ReportNoChanges()
    {
        var previous = MakeSnapshot(new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 25), Description = "GPC", Amount = -432m, RunningBalance = 900m, AccountId = 1 });
        var current = MakeSnapshot(new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 25), Description = "GPC", Amount = -432m, RunningBalance = 900m, AccountId = 1 });

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void MultipleMonthlyOccurrencesOfTheSameBill_ArePairedPositionally_ByDateOrder()
    {
        // A recurring bill visible for 2+ months within the snapshot window shares the same
        // (AccountId, Description) - must pair the Nth occurrence with the Nth occurrence
        // (by date order), not collapse them into one ambiguous match.
        var previous = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 5), Description = "GPC", Amount = -432m, RunningBalance = 900m, AccountId = 1 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 5), Description = "GPC", Amount = -432m, RunningBalance = 800m, AccountId = 1 });
        var current = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 5), Description = "GPC", Amount = -351m, RunningBalance = 950m, AccountId = 1 }, // this month already posted at the real (lower) amount
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 5), Description = "GPC", Amount = -432m, RunningBalance = 850m, AccountId = 1 }); // next month unchanged

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        var change = Assert.Single(diff.Changed);
        Assert.Equal(new DateOnly(2026, 7, 5), change.OldDate);
        Assert.Equal(-432m, change.OldAmount);
        Assert.Equal(-351m, change.NewAmount);
    }
}
