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
    public static ForecastSnapshotDiff Diff(ForecastSnapshot previous, ForecastSnapshot current)
    {
        var diff = new ForecastSnapshotDiff();

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
            var count = Math.Max(previousLines.Count, currentLines.Count);

            for (var i = 0; i < count; i++)
            {
                var previousLine = i < previousLines.Count ? previousLines[i] : null;
                var currentLine = i < currentLines.Count ? currentLines[i] : null;

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
