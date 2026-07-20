namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>What happened to one specific item within an order/refund email - lets a
/// caller (the sync progress log) show exactly what was extracted and recorded, not just
/// an aggregate count.</summary>
public record AmazonItemOutcome(string ItemTitle, decimal Price, int Quantity, bool WasDuplicate, bool NeedsReview = false);
