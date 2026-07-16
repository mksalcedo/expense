namespace Expense.Domain.Services.Categorization;

/// <summary>Thin abstraction over CategorizationService so UI components can be tested against a fake result.</summary>
public interface IReviewQueueProvider
{
    Task<ReviewQueueData> GetReviewQueueAsync(CancellationToken cancellationToken = default);

    Task<int> CategorizeTransactionAsync(int transactionId, int categoryId, string? merchantPatternToCreate, CancellationToken cancellationToken = default);

    Task<int> CategorizeAmazonItemAsync(int itemId, int categoryId, string? productPatternToCreate, CancellationToken cancellationToken = default);

    Task<ReapplyRulesResult> ReapplyRulesAsync(CancellationToken cancellationToken = default);

    Task<int> BulkCategorizeTransactionsAsync(IReadOnlyList<int> transactionIds, int categoryId, CancellationToken cancellationToken = default);

    Task<int> BulkCategorizeAmazonItemsAsync(IReadOnlyList<int> itemIds, int categoryId, CancellationToken cancellationToken = default);
}
