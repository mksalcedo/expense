using Expense.Domain.Entities;

namespace Expense.Domain.Services.Categorization;

public class ReviewQueueData
{
    public required List<BankTransaction> PendingTransactions { get; set; }
    public required List<AmazonOrderItem> PendingAmazonItems { get; set; }
    public required List<Category> Categories { get; set; }
}
