using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Transactions;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in TransactionManagementService.</summary>
public class TransactionsPageProvider(IDbContextFactory<ExpenseDbContext> contextFactory, TransactionManagementService transactions) : ITransactionsPageProvider
{
    public async Task<TransactionsPageData> GetTransactionsAsync(string? searchText, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return new TransactionsPageData
        {
            Transactions = await transactions.GetTransactionsAsync(context, searchText),
            Categories = await context.Categories.OrderBy(c => c.Name).ToListAsync(cancellationToken)
        };
    }

    public async Task UpdateCategoryAsync(int transactionId, int? categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await transactions.UpdateCategoryAsync(context, transactionId, categoryId);
    }
}
