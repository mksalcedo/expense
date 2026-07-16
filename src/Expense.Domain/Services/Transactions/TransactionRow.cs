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

    /// <summary>
    /// Amazon rows only - true when this item's title/price came from a placeholder
    /// ("(Item details unavailable...)") rather than a real parsed item, so it needs a
    /// human to look up the real order and correct it. Everything else (bank transactions,
    /// itemized Amazon orders, gift cards) is reliable and never needs this.
    /// </summary>
    public bool NeedsReview { get; set; }
}
