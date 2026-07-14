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

    /// <summary>NULL means "pending categorization" for this item (new/unrecognized product).</summary>
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public decimal? RefundAmount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
