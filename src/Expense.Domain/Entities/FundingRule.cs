namespace Expense.Domain.Entities;

/// <summary>
/// Live, user-configurable from day one - answers "which categories count toward the
/// Amex payment forecast" (see FundingStrategies). One row per category.
/// </summary>
public class FundingRule
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public required string Strategy { get; set; }
}
