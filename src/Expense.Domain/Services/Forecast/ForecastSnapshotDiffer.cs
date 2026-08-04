using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Compares two daily ForecastSnapshots to answer "what changed since yesterday" - a
/// recurring bill can appear more than once within a single snapshot's ~120-day window
/// (e.g. this month's and next month's occurrence share the same AccountId/Description),
/// so lines are grouped by (AccountId, Description) and paired positionally in date order
/// within each group - the Nth occurrence in the old snapshot pairs with the Nth occurrence
/// in the new one, rather than collapsing them into one ambiguous match.
/// </summary>
public static class ForecastSnapshotDiffer
{
    /// <summary>
    /// reconciledTransactions: real BankTransaction rows with CategoryId and
    /// ReconciledOccurrenceDate set (the same shape ForecastEngine itself queries) -
    /// optional, and when omitted every removed line is reported as a bare removal
    /// exactly as before. When supplied, a removed line whose CategoryId+Date matches a
    /// reconciled transaction is reported instead as Reconciled, with the real amount
    /// alongside what was budgeted - see ForecastSnapshotDiff.Reconciled.
    ///
    /// checkingTransactions: real checking-account BankTransaction rows (any category, or
    /// none) - optional, used only to explain a StartingBalanceChange. Filtered here by
    /// CreatedAt (not TransactionDate/PostedDate) falling strictly between the two
    /// snapshots' own CapturedAt timestamps - a transaction can post on the bank's side well
    /// before it's actually imported into the app, so "when we found out about it" is what
    /// actually explains a delta between two snapshots taken at specific moments, not "when
    /// it happened" on the bank's side.
    /// </summary>
    public static ForecastSnapshotDiff Diff(
        ForecastSnapshot previous, ForecastSnapshot current,
        IReadOnlyList<BankTransaction>? reconciledTransactions = null,
        IReadOnlyList<BankTransaction>? checkingTransactions = null)
    {
        // The caller (Forecast History's From/To pickers) doesn't enforce chronological
        // order - "From" defaults to the most recent snapshot, so picking an older one for
        // "To" without touching "From" first is a reasonable, common way to end up with the
        // later snapshot passed as `previous`. Normalize here so the rest of this method can
        // always assume previous is genuinely the earlier one.
        if (previous.CapturedAt > current.CapturedAt)
        {
            (previous, current) = (current, previous);
        }

        var diff = new ForecastSnapshotDiff();

        if (previous.StartingBalance != current.StartingBalance)
        {
            var explanatoryTransactions = (checkingTransactions ?? [])
                .Where(t => t.CreatedAt > previous.CapturedAt && t.CreatedAt <= current.CapturedAt)
                .OrderBy(t => t.PostedDate ?? t.TransactionDate)
                .Select(t => new StartingBalanceTransaction
                {
                    Date = t.PostedDate ?? t.TransactionDate, Description = t.Description, Amount = t.Amount
                })
                .ToList();

            diff.StartingBalanceChange = new StartingBalanceChange
            {
                OldBalance = previous.StartingBalance, NewBalance = current.StartingBalance, Transactions = explanatoryTransactions
            };
        }

        var previousGroups = previous.Lines
            .GroupBy(l => (l.AccountId, l.Description))
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Date).ToList());
        var currentGroups = current.Lines
            .GroupBy(l => (l.AccountId, l.Description))
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Date).ToList());

        foreach (var key in previousGroups.Keys.Union(currentGroups.Keys))
        {
            var previousLines = previousGroups.GetValueOrDefault(key, []);
            var currentLines = currentGroups.GetValueOrDefault(key, []);

            // Stage 1: match by exact date first - a recurring occurrence whose date
            // genuinely didn't change between the two snapshots produces no diff entry at
            // all, regardless of where it happens to sit in either snapshot's sorted list
            // (which shifts every time an earlier occurrence drops out or a later one enters
            // the window - see OneOccurrenceDroppingFromTheMiddleOfTheWindow_* below).
            var currentByDate = currentLines.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => g.First());
            var matchedCurrentDates = new HashSet<DateOnly>();
            var previousLeftover = new List<ForecastSnapshotLine>();

            foreach (var previousLine in previousLines)
            {
                if (currentByDate.TryGetValue(previousLine.Date, out var currentLine))
                {
                    matchedCurrentDates.Add(previousLine.Date);
                    if (previousLine.Amount != currentLine.Amount)
                    {
                        diff.Changed.Add(new ForecastLineChange
                        {
                            Description = currentLine.Description,
                            AccountId = currentLine.AccountId,
                            OldDate = previousLine.Date,
                            NewDate = currentLine.Date,
                            OldAmount = previousLine.Amount,
                            NewAmount = currentLine.Amount
                        });
                    }
                }
                else
                {
                    previousLeftover.Add(previousLine);
                }
            }

            var currentLeftover = currentLines.Where(l => !matchedCurrentDates.Contains(l.Date)).OrderBy(l => l.Date).ToList();

            // Stage 2: for whatever's left on the old side, check reconciliation BEFORE
            // falling back to positional pairing - this is what correctly attributes "this
            // occurrence was actually paid" to the occurrence that was actually paid,
            // instead of whichever leftover happens to land at a shared position after
            // stage 1 (the bug that made this feature silently never fire correctly unless
            // the paid occurrence happened to be the very last one in the list).
            var stillUnmatchedPrevious = new List<ForecastSnapshotLine>();
            foreach (var previousLine in previousLeftover)
            {
                var reconciledMatch = reconciledTransactions?.FirstOrDefault(t =>
                    t.CategoryId == previousLine.CategoryId && t.ReconciledOccurrenceDate == previousLine.Date);

                if (previousLine.CategoryId is not null && reconciledMatch is not null)
                {
                    diff.Reconciled.Add(new ReconciledLine
                    {
                        Description = previousLine.Description,
                        AccountId = previousLine.AccountId,
                        Date = previousLine.Date,
                        BudgetedAmount = previousLine.Amount,
                        ActualAmount = reconciledMatch.Amount
                    });
                }
                else
                {
                    stillUnmatchedPrevious.Add(previousLine);
                }
            }

            // Stage 3: positional fallback for whatever's genuinely left - real reschedules
            // (a deferral moving a bill's date) with nothing else to explain them, plus any
            // true net Added/Removed when the counts on each side don't line up.
            var count = Math.Max(stillUnmatchedPrevious.Count, currentLeftover.Count);
            for (var i = 0; i < count; i++)
            {
                var previousLine = i < stillUnmatchedPrevious.Count ? stillUnmatchedPrevious[i] : null;
                var currentLine = i < currentLeftover.Count ? currentLeftover[i] : null;

                if (previousLine is null && currentLine is not null)
                {
                    diff.Added.Add(currentLine);
                }
                else if (previousLine is not null && currentLine is null)
                {
                    diff.Removed.Add(previousLine);
                }
                else if (previousLine!.Date != currentLine!.Date || previousLine.Amount != currentLine.Amount)
                {
                    diff.Changed.Add(new ForecastLineChange
                    {
                        Description = currentLine.Description,
                        AccountId = currentLine.AccountId,
                        OldDate = previousLine.Date,
                        NewDate = currentLine.Date,
                        OldAmount = previousLine.Amount,
                        NewAmount = currentLine.Amount
                    });
                }
            }
        }

        return diff;
    }
}
