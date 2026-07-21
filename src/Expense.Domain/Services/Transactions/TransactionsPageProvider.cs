using Expense.Domain.Data;
using Expense.Domain.Services.Categories;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Transactions;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in TransactionManagementService.</summary>
public class TransactionsPageProvider(IDbContextFactory<ExpenseDbContext> contextFactory, TransactionManagementService transactions) : ITransactionsPageProvider
{
    public async Task<TransactionsPageData> GetTransactionsAsync(
        string? searchText, int? categoryFilter, bool needsReviewOnly = false, TransactionSource? sourceFilter = null,
        int? accountFilter = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var result = await transactions.GetTransactionsAsync(context, searchText, categoryFilter, needsReviewOnly, sourceFilter, accountFilter, page, pageSize);
        return new TransactionsPageData
        {
            Transactions = result.Items,
            TotalCount = result.TotalCount,
            Categories = await context.Categories.OrderBy(c => c.Name).ToListAsync(cancellationToken),
            // Every account, not just active ones - a historical/paid-off account's past
            // transactions are still real data worth being able to filter by.
            Accounts = await context.Accounts.OrderBy(a => a.Name)
                .Select(a => new AccountOption { Id = a.Id, Name = a.Name })
                .ToListAsync(cancellationToken)
        };
    }

    public async Task UpdateCategoryAsync(TransactionSource source, int id, int? categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await transactions.UpdateCategoryAsync(context, source, id, categoryId);
    }

    public async Task<int> BulkCategorizeAsync(IReadOnlyList<int> bankTransactionIds, IReadOnlyList<int> amazonItemIds, int categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await transactions.BulkCategorizeAsync(context, bankTransactionIds, amazonItemIds, categoryId);
    }

    public async Task UpdateAmazonItemDetailsAsync(int itemId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await transactions.UpdateAmazonItemDetailsAsync(context, itemId, itemTitle, price, quantity);
    }
}
