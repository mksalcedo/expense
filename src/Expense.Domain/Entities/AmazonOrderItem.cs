namespace Expense.Domain.Entities;

public class AmazonOrderItem
{
    public int Id { get; set; }

    /// <summary>Plain text, not a FK to a parent orders table - deliberately, see design-summary.md.</summary>
    public required string OrderId { get; set; }

    public DateOnly OrderDate { get; set; }
    public required string ItemTitle { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }

    /// <summary>This item's proportional share of the order's tax/shipping leftover.</summary>
    public decimal TaxAllocated { get; set; }

    /// <summary>NULL for a new/unrecognized product, or for anything bulk-categorized (see BulkCategorizeAmazonItemsAsync) - not the "pending" signal, see CategoryId.</summary>
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>NULL means "pending categorization" for this item - the real status signal, same convention as BankTransaction.CategoryId.</summary>
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public decimal? RefundAmount { get; set; }

    /// <summary>
    /// True when the title/price came from a "simplified" order confirmation email with no
    /// real item list (see AmazonOrderEmailParser.ParseSimplifiedOrder) - the data is a
    /// placeholder, not a parsing guess about a real item, so it needs a human to look up
    /// the real order and correct it. False for everything else (itemized orders and gift
    /// cards are both reliable, since the parser fails loudly rather than guessing).
    /// </summary>
    public bool NeedsReview { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
