namespace Expense.Domain.Services.Forecast;

/// <summary>
/// A single dated forecast-ledger row. Amount is signed: positive for income/credit,
/// negative for expense/debit - never persisted, always generated on demand.
/// </summary>
public class LedgerLine
{
    public required DateOnly Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public int AccountId { get; set; }

    /// <summary>Propagated from RecurringRule.CategoryId - see there. Null for one-time events.</summary>
    public int? CategoryId { get; set; }

    /// <summary>
    /// How many days before/after this line's Date a real matching transaction still
    /// counts as satisfying it - see RecurrenceExpander.MatchWindowDaysFor. Meaningless
    /// when CategoryId is null.
    /// </summary>
    public int MatchWindowDays { get; set; }

    /// <summary>Set only for a line derived from a OneTimeEvent - lets ForecastEngine tell
    /// apart an unrelated one-time event from the recurring bill it happens to share an
    /// (AccountId, Date) with, so a partial payment's own auto-created event never gets
    /// mistaken for the bill it was paid against (see the PartialPayment doc comment).</summary>
    public int? SourceOneTimeEventId { get; set; }
}
