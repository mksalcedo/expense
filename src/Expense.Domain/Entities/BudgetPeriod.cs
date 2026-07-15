namespace Expense.Domain.Entities;

/// <summary>
/// Dated-versioned budget target for a category. Amount can be entered in any
/// frequency (not a fixed unit) - BudgetProrationService converts to whatever
/// period a caller actually needs.
/// </summary>
public class BudgetPeriod
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public decimal Amount { get; set; }
    public Frequency Frequency { get; set; }
    public Direction Direction { get; set; } = Direction.Expense;

    /// <summary>
    /// A reference date the recurrence is computed from, for categories whose funding
    /// strategy is Direct (RecurrenceExpander needs this to place dated ledger lines).
    /// Null for categories that aren't directly expanded (e.g. ordinary spending budgets).
    /// </summary>
    public DateOnly? Anchor { get; set; }

    /// <summary>
    /// The account this budget period directly affects, for Direct-strategy categories
    /// (e.g. which checking account a paycheck lands in or a bill is paid from).
    /// </summary>
    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null means this is the current/active budget for the category.</summary>
    public DateOnly? EffectiveThrough { get; set; }
}
