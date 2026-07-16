namespace Expense.Domain.Services.Transactions;

/// <summary>Thin abstraction over TransactionManagementService so UI components can be tested against a fake result.</summary>
public interface ITransactionsPageProvider
{
    Task<TransactionsPageData> GetTransactionsAsync(string? searchText, CancellationToken cancellationToken = default);

    Task UpdateCategoryAsync(int transactionId, int? categoryId, CancellationToken cancellationToken = default);
}
