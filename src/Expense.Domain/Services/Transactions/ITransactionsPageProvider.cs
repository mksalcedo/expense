namespace Expense.Domain.Services.Transactions;

/// <summary>Thin abstraction over TransactionManagementService so UI components can be tested against a fake result.</summary>
public interface ITransactionsPageProvider
{
    Task<TransactionsPageData> GetTransactionsAsync(
        string? searchText, int? categoryFilter, bool needsReviewOnly = false, TransactionSource? sourceFilter = null,
        int? accountFilter = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    Task UpdateCategoryAsync(TransactionSource source, int id, int? categoryId, CancellationToken cancellationToken = default);

    Task<int> BulkCategorizeAsync(IReadOnlyList<int> bankTransactionIds, IReadOnlyList<int> amazonItemIds, int categoryId, CancellationToken cancellationToken = default);

    Task UpdateAmazonItemDetailsAsync(int itemId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default);
}
