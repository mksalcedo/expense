using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;

namespace Expense.Domain.Tests.Services.Forecast;

public class ForecastSnapshotDifferTests
{
    private static ForecastSnapshot MakeSnapshot(decimal startingBalance, DateTimeOffset? capturedAt = null, params ForecastSnapshotLine[] lines) => new()
    {
        AsOfDate = new DateOnly(2026, 7, 23), StartingBalance = startingBalance, LowestProjectedBalance = 500m,
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow, Lines = lines.ToList()
    };

    private static ForecastSnapshot MakeSnapshot(params ForecastSnapshotLine[] lines) => MakeSnapshot(1000m, null, lines);

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
    public void MultipleMonthlyOccurrencesOfTheSameBill_WithMatchingDates_AreComparedByDate()
    {
        // A recurring bill visible for 2+ months within the snapshot window shares the same
        // (AccountId, Description) - each occurrence is matched to its counterpart by exact
        // date first (see the scenarios below for what happens when dates *don't* line up).
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

    // Real gap this guards (user-identified, found live 2026-08-03): the starting balance
    // change was reported as a bare number with no way to see what actually moved it - a
    // user had to be handed a manually-run SQL query to find out. Checking-account
    // transactions *created* (not necessarily dated) between the two snapshots' own capture
    // timestamps explain the delta, since a transaction can post on the bank's side well
    // before it's actually visible in the app (a real Water bill posted 7/31 but wasn't
    // imported until the 8/2 sync).
    [Fact]
    public void StartingBalanceChange_IncludesCheckingTransactionsCreatedBetweenTheTwoSnapshots()
    {
        var previousCapturedAt = new DateTimeOffset(2026, 8, 1, 19, 0, 0, TimeSpan.Zero);
        var currentCapturedAt = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
        var previous = MakeSnapshot(4649.18m, previousCapturedAt);
        var current = MakeSnapshot(4209.21m, currentCapturedAt);
        var checkingTransactions = new List<BankTransaction>
        {
            new()
            {
                AccountId = 1, TransactionDate = new DateOnly(2026, 7, 31), PostedDate = new DateOnly(2026, 7, 31),
                Description = "GWINNETT CTY WATER", Amount = -378.20m, ImportSource = "Plaid",
                CreatedAt = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)
            }
        };

        var diff = ForecastSnapshotDiffer.Diff(previous, current, checkingTransactions: checkingTransactions);

        var txn = Assert.Single(diff.StartingBalanceChange!.Transactions);
        Assert.Equal("GWINNETT CTY WATER", txn.Description);
        Assert.Equal(-378.20m, txn.Amount);
        Assert.Equal(new DateOnly(2026, 7, 31), txn.Date);
    }

    [Fact]
    public void StartingBalanceChange_ExcludesTransactionsCreatedOutsideTheSnapshotWindow()
    {
        var previousCapturedAt = new DateTimeOffset(2026, 8, 1, 19, 0, 0, TimeSpan.Zero);
        var currentCapturedAt = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
        var previous = MakeSnapshot(4649.18m, previousCapturedAt);
        var current = MakeSnapshot(4209.21m, currentCapturedAt);
        var checkingTransactions = new List<BankTransaction>
        {
            // Created before the previous snapshot - already reflected in its own balance.
            new()
            {
                AccountId = 1, TransactionDate = new DateOnly(2026, 7, 29), PostedDate = new DateOnly(2026, 7, 29),
                Description = "Old charge", Amount = -12.90m, ImportSource = "Plaid",
                CreatedAt = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)
            },
            // Created after the current snapshot - not part of this delta yet.
            new()
            {
                AccountId = 1, TransactionDate = new DateOnly(2026, 8, 3), PostedDate = new DateOnly(2026, 8, 3),
                Description = "Future charge", Amount = -5.00m, ImportSource = "Plaid",
                CreatedAt = new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero)
            }
        };

        var diff = ForecastSnapshotDiffer.Diff(previous, current, checkingTransactions: checkingTransactions);

        Assert.Empty(diff.StartingBalanceChange!.Transactions);
    }

    [Fact]
    public void StartingBalanceChange_WithNoCheckingTransactionsSupplied_HasAnEmptyTransactionsList()
    {
        var previous = MakeSnapshot(4488.63m);
        var current = MakeSnapshot(4418.31m);

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        Assert.Empty(diff.StartingBalanceChange!.Transactions);
    }

    // Real bug found live (2026-08-03): the Forecast History page's From/To pickers don't
    // enforce chronological order - the "From" dropdown already defaults to the most recent
    // snapshot, so a user filling in "To" with an older one (a reasonable thing to do without
    // touching "From" first) ends up passing the newer snapshot as `previous` and the older
    // one as `current`. Diff must give the same, correct answer regardless of which literal
    // parameter position the earlier/later snapshot lands in.
    [Fact]
    public void Diff_CalledWithSnapshotsInReverseChronologicalOrder_StillComputesTheCorrectDirection()
    {
        var earlierCapturedAt = new DateTimeOffset(2026, 8, 1, 19, 0, 0, TimeSpan.Zero);
        var laterCapturedAt = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
        var earlier = MakeSnapshot(4649.18m, earlierCapturedAt);
        var later = MakeSnapshot(4209.21m, laterCapturedAt);

        // Passed backward: the "previous" parameter actually holds the later snapshot.
        var diff = ForecastSnapshotDiffer.Diff(later, earlier);

        Assert.Equal(4649.18m, diff.StartingBalanceChange!.OldBalance);
        Assert.Equal(4209.21m, diff.StartingBalanceChange!.NewBalance);
        Assert.Equal(-439.97m, diff.StartingBalanceChange!.Delta);
    }

    [Fact]
    public void Diff_CalledWithSnapshotsInReverseChronologicalOrder_StillFindsTheExplanatoryTransactions()
    {
        var earlierCapturedAt = new DateTimeOffset(2026, 8, 1, 19, 0, 0, TimeSpan.Zero);
        var laterCapturedAt = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
        var earlier = MakeSnapshot(4649.18m, earlierCapturedAt);
        var later = MakeSnapshot(4209.21m, laterCapturedAt);
        var checkingTransactions = new List<BankTransaction>
        {
            new()
            {
                AccountId = 1, TransactionDate = new DateOnly(2026, 7, 31), PostedDate = new DateOnly(2026, 7, 31),
                Description = "GWINNETT CTY WATER", Amount = -378.20m, ImportSource = "Plaid",
                CreatedAt = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)
            }
        };

        var diff = ForecastSnapshotDiffer.Diff(later, earlier, checkingTransactions: checkingTransactions);

        var txn = Assert.Single(diff.StartingBalanceChange!.Transactions);
        Assert.Equal("GWINNETT CTY WATER", txn.Description);
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

    // Real bug this guards (user-identified, found live 2026-08-03): when the earliest
    // occurrence in a recurring group drops out (paid, or reconciled), the *old* positional
    // pairing shifted every remaining occurrence up one slot and reported all of them as
    // spurious "Changed" entries, even though none of them actually moved. Matching by exact
    // date first means an occurrence whose date genuinely didn't change produces no diff
    // entry at all, regardless of where it sits in either list.
    [Fact]
    public void OneOccurrenceDroppingFromTheMiddleOfTheWindow_DoesNotFalselyReportTheRestAsChanged()
    {
        var previous = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 30), Description = "Water", Amount = -193m, RunningBalance = 900m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 20), Description = "Water", Amount = -193m, RunningBalance = 800m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 9, 20), Description = "Water", Amount = -193m, RunningBalance = 700m, AccountId = 1, CategoryId = 27 });
        var current = MakeSnapshot(
            // 7/30 dropped (paid); 8/20 and 9/20 are exactly as they were - no new occurrence
            // entered the window in this comparison, so there's nothing ambiguous for the
            // dropped one to be mistaken for a reschedule of.
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 20), Description = "Water", Amount = -193m, RunningBalance = 750m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 9, 20), Description = "Water", Amount = -193m, RunningBalance = 650m, AccountId = 1, CategoryId = 27 });

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Added);
        var removed = Assert.Single(diff.Removed);
        Assert.Equal(new DateOnly(2026, 7, 30), removed.Date);
    }

    [Fact]
    public void ANewOccurrenceEnteringTheWindow_WithNothingElseChanging_IsReportedAsCleanlyAdded()
    {
        var previous = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 20), Description = "Water", Amount = -193m, RunningBalance = 800m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 9, 20), Description = "Water", Amount = -193m, RunningBalance = 700m, AccountId = 1, CategoryId = 27 });
        var current = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 20), Description = "Water", Amount = -193m, RunningBalance = 750m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 9, 20), Description = "Water", Amount = -193m, RunningBalance = 650m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 10, 20), Description = "Water", Amount = -193m, RunningBalance = 550m, AccountId = 1, CategoryId = 27 });

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Removed);
        var added = Assert.Single(diff.Added);
        Assert.Equal(new DateOnly(2026, 10, 20), added.Date);
    }

    // The one genuinely ambiguous case: an occurrence disappears with no reconciliation
    // evidence to explain it, AND a new one appears in the same comparison, leaving exactly
    // one unexplained leftover on each side. There's no persistent occurrence ID to tell
    // "these are the same bill, rescheduled" apart from "these are two unrelated events that
    // happened to net out to the same count" - positional fallback assumes the former,
    // matching this diff tool's original intent for genuine reschedules (see
    // LineWithADifferentDate_IsReportedAsChanged_NotRemovedAndAdded above). In practice this
    // is rare: a real drop is almost always either explained by reconciliation (stage 2
    // catches it) or leaves the counts genuinely uneven (the two tests above).
    [Fact]
    public void AnUnexplainedDropWithNoReconciliation_AndANewEntryInTheSameComparison_IsTreatedAsAReschedule()
    {
        var previous = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 30), Description = "Water", Amount = -193m, RunningBalance = 900m, AccountId = 1, CategoryId = 27 });
        var current = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 10, 20), Description = "Water", Amount = -193m, RunningBalance = 550m, AccountId = 1, CategoryId = 27 });

        var diff = ForecastSnapshotDiffer.Diff(previous, current);

        var changed = Assert.Single(diff.Changed);
        Assert.Equal(new DateOnly(2026, 7, 30), changed.OldDate);
        Assert.Equal(new DateOnly(2026, 10, 20), changed.NewDate);
    }

    [Fact]
    public void OneOccurrenceDroppingFromTheMiddleOfTheWindow_CorrectlyAttributesReconciliationToTheRightDate()
    {
        // Same shape as above, but the dropped occurrence actually did reconcile against a
        // real transaction - it must be reported as Reconciled (with the right variance),
        // not swallowed into a "Removed" for whichever occurrence happens to fall off the
        // tail of the positionally-shifted list (the bug that made the earlier version of
        // this feature silently never fire for anything but the last occurrence).
        var previous = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 30), Description = "Water", Amount = -193m, RunningBalance = 900m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 20), Description = "Water", Amount = -193m, RunningBalance = 800m, AccountId = 1, CategoryId = 27 });
        var current = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 20), Description = "Water", Amount = -193m, RunningBalance = 750m, AccountId = 1, CategoryId = 27 });
        var reconciledTransactions = new List<BankTransaction>
        {
            new()
            {
                AccountId = 1, TransactionDate = new DateOnly(2026, 7, 31), PostedDate = new DateOnly(2026, 7, 31),
                Description = "GWINNETT CTY WATER", Amount = -378.20m, CategoryId = 27, ReconciledOccurrenceDate = new DateOnly(2026, 7, 30),
                ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var diff = ForecastSnapshotDiffer.Diff(previous, current, reconciledTransactions);

        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Removed);
        var reconciled = Assert.Single(diff.Reconciled);
        Assert.Equal(new DateOnly(2026, 7, 30), reconciled.Date);
        Assert.Equal(-193m, reconciled.BudgetedAmount);
        Assert.Equal(-378.20m, reconciled.ActualAmount);
    }

    [Fact]
    public void SimultaneousReconciliationAndReschedule_InTheSameWindow_AreNotCrossMatchedWithEachOther()
    {
        // The trickiest case: one occurrence reconciles AND a different occurrence in the
        // same group gets deferred to a new date, in the same comparison window. A naive
        // "date-match then positionally pair whatever's left" approach would wrongly pair
        // the reconciled occurrence's old date with the deferred occurrence's new date
        // (both being simple leftovers) unless the reconciled one is pulled out first.
        var previous = MakeSnapshot(
            new ForecastSnapshotLine { Date = new DateOnly(2026, 7, 30), Description = "Water", Amount = -193m, RunningBalance = 900m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 20), Description = "Water", Amount = -193m, RunningBalance = 800m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 9, 20), Description = "Water", Amount = -193m, RunningBalance = 700m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 10, 20), Description = "Water", Amount = -193m, RunningBalance = 600m, AccountId = 1, CategoryId = 27 });
        var current = MakeSnapshot(
            // 7/30 reconciled (paid); 8/20 and 9/20 unchanged; 10/20 deferred to 10/25; 11/20 newly entered the window.
            new ForecastSnapshotLine { Date = new DateOnly(2026, 8, 20), Description = "Water", Amount = -193m, RunningBalance = 750m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 9, 20), Description = "Water", Amount = -193m, RunningBalance = 650m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 10, 25), Description = "Water", Amount = -193m, RunningBalance = 550m, AccountId = 1, CategoryId = 27 },
            new ForecastSnapshotLine { Date = new DateOnly(2026, 11, 20), Description = "Water", Amount = -193m, RunningBalance = 450m, AccountId = 1, CategoryId = 27 });
        var reconciledTransactions = new List<BankTransaction>
        {
            new()
            {
                AccountId = 1, TransactionDate = new DateOnly(2026, 7, 31), PostedDate = new DateOnly(2026, 7, 31),
                Description = "GWINNETT CTY WATER", Amount = -378.20m, CategoryId = 27, ReconciledOccurrenceDate = new DateOnly(2026, 7, 30),
                ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var diff = ForecastSnapshotDiffer.Diff(previous, current, reconciledTransactions);

        var reconciled = Assert.Single(diff.Reconciled);
        Assert.Equal(new DateOnly(2026, 7, 30), reconciled.Date);

        var changed = Assert.Single(diff.Changed);
        Assert.Equal(new DateOnly(2026, 10, 20), changed.OldDate);
        Assert.Equal(new DateOnly(2026, 10, 25), changed.NewDate);

        var added = Assert.Single(diff.Added);
        Assert.Equal(new DateOnly(2026, 11, 20), added.Date);

        Assert.Empty(diff.Removed);
    }
}
