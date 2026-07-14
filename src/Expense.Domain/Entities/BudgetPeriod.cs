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

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null means this is the current/active budget for the category.</summary>
    public DateOnly? EffectiveThrough { get; set; }
}
