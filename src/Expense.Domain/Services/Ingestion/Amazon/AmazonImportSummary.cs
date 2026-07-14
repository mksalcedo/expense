namespace Expense.Domain.Services.Ingestion.Amazon;

public class AmazonImportSummary
{
    public int ItemsAdded { get; set; }
    public int DuplicatesSkipped { get; set; }
    public int RefundsApplied { get; set; }
    public List<string> UnmatchedRefunds { get; } = [];
}
