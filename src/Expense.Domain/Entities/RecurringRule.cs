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

    public bool Active { get; set; } = true;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
