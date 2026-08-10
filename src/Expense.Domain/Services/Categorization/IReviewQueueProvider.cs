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

    Task DismissTransactionsAsync(IReadOnlyList<int> transactionIds, CancellationToken cancellationToken = default);

    Task DismissAmazonItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default);

    Task UpdateAmazonItemDetailsAsync(int itemId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default);

    Task AddManualAmazonItemAsync(string orderId, DateOnly orderDate, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts item title(s) from a screenshot of the real Amazon order-details page - for a
    /// NeedsReview item whose confirmation email never had item-level detail at all. Pure
    /// extraction only, doesn't save anything - the caller applies the result via
    /// UpdateAmazonItemDetailsAsync itself, same as if the user had typed it in by hand.
    /// </summary>
    Task<List<string>> ParseAmazonItemScreenshotAsync(byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default);
}
