using Expense.Domain.Entities;

namespace Expense.Domain.Services.Categorization;

public class ReviewQueueData
{
    public required List<PendingTransactionGroup> TransactionGroups { get; set; }
    public required List<PendingAmazonItemGroup> AmazonItemGroups { get; set; }
    public required List<Category> Categories { get; set; }
}
