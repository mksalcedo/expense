using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;

namespace Expense.Domain.Tests.Services.Forecast;

public class ForecastSnapshotDifferTests
{
    private static ForecastSnapshot MakeSnapshot(decimal startingBalance, params ForecastSnapshotLine[] lines) => new()
    {
        AsOfDate = new DateOnly(2026, 7, 23), StartingBalance = startingBalance, LowestProjectedBalance = 500m, Lines = lines.ToList()
    };

    private static ForecastSnapshot MakeSnapshot(params ForecastSnapshotLine[] lines) => MakeSnapshot(1000m, lines);

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

    // Real gap this guards (found live 2026-07-29): the starting balance is often the
    // single biggest driver of a shifted minimum balance (it's re-pulled fresh from the
    // latest real checking balance on every generation), but the diff only ever compared
    // .Lines - a perfect line-level diff would still silently miss "your starting balance
    // dropped $70," which is often the actual headline reason. See
    // docs/forecast-history-redesign-plan.md.
    [Fact]
    public void DifferentStartingBalance_IsReportedExplicitly()
    {
        var previous = MakeSnapshot(4488.63m);
        var current = MakeSnapshot(4418.31m);

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        Assert.NotNull(diff.StartingBalanceChange);
        Assert.Equal(4488.63m, diff.StartingBalanceChange!.OldBalance);
        Assert.Equal(4418.31m, diff.StartingBalanceChange!.NewBalance);
        Assert.Equal(-70.32m, diff.StartingBalanceChange!.Delta);
    }

    [Fact]
    public void SameStartingBalance_ReportsNoChange()
    {
        var previous = MakeSnapshot(1000m);
        var current = MakeSnapshot(1000m);

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        Assert.Null(diff.StartingBalanceChange);
    }

    // Real gap this guards (found live 2026-07-30, user-identified): once a real
    // transaction reconciles against a forecasted occurrence, ForecastEngine doesn't
    // adjust its amount - it just excludes the line entirely (it's already reflected in
    // the real starting balance; keeping the projected line too would double-count it). A
    // $150 budgeted item that actually cost $162.37 disappeared with no record of the
    // $12.37 variance - real money affecting every downstream running balance. See
    // docs/forecast-history-redesign-plan.md.
    [Fact]
    public void RemovedLineMatchingAReconciledTransaction_IsReportedAsReconciled_WithTheVariance_NotABareRemoval()
    {
        var previous = MakeSnapshot(new ForecastSnapshotLine
        {
            Date = new DateOnly(2026, 7, 25), Description = "Groceries", Amount = -150m, RunningBalance = 850m, AccountId = 1, CategoryId = 7
        });
        var current = MakeSnapshot();
        var reconciledTransactions = new List<BankTransaction>
        {
            new()
            {
                AccountId = 1, TransactionDate = new DateOnly(2026, 7, 25), PostedDate = new DateOnly(2026, 7, 25),
                Description = "PUBLIX", Amount = -162.37m, CategoryId = 7, ReconciledOccurrenceDate = new DateOnly(2026, 7, 25),
                ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var diff = ForecastSnapshotDiffer.Diff(previous, current, reconciledTransactions);

        Assert.Empty(diff.Removed);
        var reconciled = Assert.Single(diff.Reconciled);
        Assert.Equal("Groceries", reconciled.Description);
        Assert.Equal(-150m, reconciled.BudgetedAmount);
        Assert.Equal(-162.37m, reconciled.ActualAmount);
        Assert.Equal(12.37m, reconciled.Variance);
    }

    [Fact]
    public void RemovedLineWithNoMatchingReconciledTransaction_StaysInRemoved()
    {
        var previous = MakeSnapshot(new ForecastSnapshotLine
        {
            Date = new DateOnly(2026, 7, 25), Description = "Groceries", Amount = -150m, RunningBalance = 850m, AccountId = 1, CategoryId = 7
        });
        var current = MakeSnapshot();
        var reconciledTransactions = new List<BankTransaction>
        {
            // Different category - not a match, even with the same date/account.
            new()
            {
                AccountId = 1, TransactionDate = new DateOnly(2026, 7, 25), PostedDate = new DateOnly(2026, 7, 25),
                Description = "Unrelated", Amount = -50m, CategoryId = 99, ReconciledOccurrenceDate = new DateOnly(2026, 7, 25),
                ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var diff = ForecastSnapshotDiffer.Diff(previous, current, reconciledTransactions);

        Assert.Empty(diff.Reconciled);
        var removed = Assert.Single(diff.Removed);
        Assert.Equal("Groceries", removed.Description);
    }

    [Fact]
    public void RemovedLine_WithNoReconciledTransactionsSupplied_StillReportedAsRemoved()
    {
        var previous = MakeSnapshot(new ForecastSnapshotLine
        {
            Date = new DateOnly(2026, 7, 25), Description = "Groceries", Amount = -150m, RunningBalance = 850m, AccountId = 1, CategoryId = 7
        });
        var current = MakeSnapshot();

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        Assert.Empty(diff.Reconciled);
        Assert.Single(diff.Removed);
    }
}
