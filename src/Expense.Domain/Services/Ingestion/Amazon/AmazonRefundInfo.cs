namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>A parsed refund, matched back to its original amazon_order_items row by OrderId + ItemTitle.</summary>
public class AmazonRefundInfo
{
    public required string OrderId { get; set; }
    public required string ItemTitle { get; set; }
    public int Quantity { get; set; }

    /// <summary>Item refund + item tax refund combined, mirroring Price+TaxAllocated on the original purchase.</summary>
    public decimal RefundAmount { get; set; }
}
