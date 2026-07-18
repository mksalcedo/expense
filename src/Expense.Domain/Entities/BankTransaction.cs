namespace Expense.Domain.Entities;

public class BankTransaction
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public DateOnly TransactionDate { get; set; }

    /// <summary>
    /// Null until the charge posts. That's the entire "pending vs posted" distinction -
    /// import plumbing, never a user-facing status (see design-summary.md).
    /// </summary>
    public DateOnly? PostedDate { get; set; }

    public required string Description { get; set; }
    public string? Merchant { get; set; }
    public decimal Amount { get; set; }

    public string? ExternalId { get; set; }
    public required string ImportSource { get; set; }

    /// <summary>
    /// Only populated when ExternalId isn't available: a fingerprint of
    /// account + posted date + amount + normalized description.
    /// </summary>
    public string? DedupFingerprint { get; set; }

    /// <summary>
    /// NULL means "pending categorization" - no separate status column, just a filter.
    /// Amazon-merchant rows never get a value here; their category lives entirely at
    /// the amazon_order_items level (the Amex forecast never needs this, only the
    /// Spending Tracker does, and it reads Amazon detail from a different table).
    /// </summary>
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public bool IsAmazonMerchant { get; set; }

    /// <summary>
    /// True when a human chose "review later" on the Review Queue - stays uncategorized
    /// (CategoryId is still the real pending signal) but is hidden from the Review Queue's
    /// action list. Still counted in Spending Tracker's Pending total and still visible/
    /// correctable via the Transactions page's Uncategorized filter - dismissing only
    /// declutters the queue, it never hides the transaction from the app entirely.
    /// </summary>
    public bool Dismissed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
