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
}
