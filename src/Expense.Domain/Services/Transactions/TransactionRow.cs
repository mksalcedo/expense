namespace Expense.Domain.Services.Transactions;

public class TransactionRow
{
    public required TransactionSource Source { get; set; }
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    /// <summary>Amazon rows only - lets the user look the real order up on amazon.com.</summary>
    public string? OrderId { get; set; }

    /// <summary>Amazon rows only - editable, to fix a placeholder/incomplete item from a parsed email.</summary>
    public decimal? Price { get; set; }

    /// <summary>Amazon rows only - editable, same reason as Price.</summary>
    public int? Quantity { get; set; }
}
