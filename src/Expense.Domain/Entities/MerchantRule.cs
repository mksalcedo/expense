namespace Expense.Domain.Entities;

/// <summary>
/// Applies across accounts - e.g. a Kroger rule matches whether the charge was on
/// checking or Amex.
/// </summary>
public class MerchantRule
{
    public int Id { get; set; }
    public required string MerchantPattern { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    /// <summary>
    /// Restricts the rule to transactions of one sign: <see cref="Direction.Income"/> matches
    /// only money in (Amount &gt;= 0), <see cref="Direction.Expense"/> only money out. Null (the
    /// default, and every rule created before this existed) matches either sign. Lets one
    /// merchant string route two ways - e.g. a bare "Venmo" that is a card payment when money
    /// goes out but piano income when money comes in.
    /// </summary>
    public Direction? Direction { get; set; }
}
