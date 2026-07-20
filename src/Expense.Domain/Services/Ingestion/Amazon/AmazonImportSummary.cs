namespace Expense.Domain.Services.Ingestion.Amazon;

public class AmazonImportSummary
{
    public int ItemsAdded { get; set; }
    public int DuplicatesSkipped { get; set; }
    public int RefundsApplied { get; set; }
    public int RefundDuplicatesSkipped { get; set; }

    /// <summary>One entry per item/refund actually seen in this call, added or not - for progress reporting.</summary>
    public List<AmazonItemOutcome> ItemOutcomes { get; } = [];
}
