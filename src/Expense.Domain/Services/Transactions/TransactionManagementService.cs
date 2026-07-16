using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Transactions;

/// <summary>
/// Browses and edits ALL bank transactions, not just pending ones - the Review Queue
/// only ever shows uncategorized rows, so there was previously no way to find and fix a
/// transaction that had been categorized incorrectly. Amazon-merchant transactions are
/// excluded: their category lives entirely at the amazon_order_items level, never here.
/// </summary>
public class TransactionManagementService
{
    public async Task<List<TransactionRow>> GetTransactionsAsync(ExpenseDbContext context, string? searchText)
    {
        var transactions = await context.BankTransactions
            .Include(t => t.Category)
            .Where(t => !t.IsAmazonMerchant)
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            transactions = transactions
                .Where(t => t.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return transactions
            .Select(t => new TransactionRow
            {
                Id = t.Id,
                Date = t.TransactionDate,
                Description = t.Description,
                Amount = t.Amount,
                CategoryId = t.CategoryId,
                CategoryName = t.Category?.Name
            })
            .ToList();
    }

    public async Task UpdateCategoryAsync(ExpenseDbContext context, int transactionId, int? categoryId)
    {
        var transaction = await context.BankTransactions.SingleAsync(t => t.Id == transactionId);
        transaction.CategoryId = categoryId;
        await context.SaveChangesAsync();
    }
}
