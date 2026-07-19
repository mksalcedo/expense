namespace Expense.Domain.Entities;

/// <summary>
/// Plain in-memory DTO consumed by RecurrenceExpander - not an EF entity/table. Built at
/// forecast-generation time from Category + BudgetPeriod (FundingRule.Strategy == Direct)
/// and, for debt accounts, synthesized directly in ForecastEngine.
/// </summary>
public class RecurringRule
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public Direction Direction { get; set; }
    public decimal Amount { get; set; }
    public Frequency Frequency { get; set; }

    /// <summary>
    /// A reference date the recurrence is computed from - e.g. the day-of-month for
    /// Monthly/Quarterly/Annual, or the actual date to count Weekly/Biweekly intervals from.
    /// </summary>
    public DateOnly Anchor { get; set; }

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    /// <summary>
    /// The category real bank transactions for this obligation get tagged with (the
    /// BudgetPeriod's own category for Direct rules, or the linked "X Payment" category
    /// via FundingRule.AccountId for a debt account's synthetic rule) - lets the forecast
    /// tell whether a given occurrence has actually happened yet. Null if there's no such
    /// link (occurrence is always shown as still-projected, the old behavior).
    /// </summary>
    public int? CategoryId { get; set; }

    public bool Active { get; set; } = true;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
