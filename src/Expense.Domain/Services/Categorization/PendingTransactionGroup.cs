namespace Expense.Domain.Services.Categorization;

public class PendingTransactionGroup
{
    public required string SuggestedPattern { get; set; }
    public required string SampleDescription { get; set; }
    public required List<int> TransactionIds { get; set; }
    public decimal TotalAmount { get; set; }
}
