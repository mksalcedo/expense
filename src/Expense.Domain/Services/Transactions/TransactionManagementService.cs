using Expense.Domain.Data;
using Expense.Domain.Services.Categorization;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Transactions;

/// <summary>
/// Browses and edits every categorizable transaction - bank transactions AND itemized
/// Amazon purchases together - not just pending ones (the Review Queue only ever shows
/// uncategorized rows, so there was previously no way to find and fix something that had
/// already been categorized incorrectly). The raw "AMAZON MARKETPLACE"-style bank_transaction
/// row is excluded for any Amazon purchase - its real detail lives in amazon_order_items,
/// which appears here instead.
/// </summary>
public class TransactionManagementService(CategorizationService categorization)
{
    /// <summary>Sentinel categoryFilter value meaning "uncategorized only" - real category IDs are always positive.</summary>
    public const int UncategorizedFilterValue = 0;

    public async Task<List<TransactionRow>> GetTransactionsAsync(ExpenseDbContext context, string? searchText, int? categoryFilter)
    {
        var bankTransactions = await context.BankTransactions
            .Include(t => t.Category)
            .Where(t => !t.IsAmazonMerchant)
            .ToListAsync();

        var amazonItems = await context.AmazonOrderItems
            .Include(i => i.Category)
            .ToListAsync();

        var rows = new List<TransactionRow>();
        rows.AddRange(bankTransactions.Select(t => new TransactionRow
        {
            Source = TransactionSource.Bank,
            Id = t.Id,
            Date = t.TransactionDate,
            Description = t.Description,
            Amount = t.Amount,
            CategoryId = t.CategoryId,
            CategoryName = t.Category?.Name
        }));
        rows.AddRange(amazonItems.Select(i => new TransactionRow
        {
            Source = TransactionSource.Amazon,
            Id = i.Id,
            Date = i.OrderDate,
            Description = i.ItemTitle,
            Amount = -(i.Price * i.Quantity + i.TaxAllocated - (i.RefundAmount ?? 0m)),
            CategoryId = i.CategoryId,
            CategoryName = i.Category?.Name,
            OrderId = i.OrderId,
            Price = i.Price,
            Quantity = i.Quantity
        }));

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            rows = rows.Where(r => r.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (categoryFilter == UncategorizedFilterValue)
        {
            rows = rows.Where(r => r.CategoryId == null).ToList();
        }
        else if (categoryFilter is { } categoryId)
        {
            rows = rows.Where(r => r.CategoryId == categoryId).ToList();
        }

        return rows.OrderByDescending(r => r.Date).ToList();
    }

    public async Task UpdateCategoryAsync(ExpenseDbContext context, TransactionSource source, int id, int? categoryId)
    {
        if (source == TransactionSource.Bank)
        {
            var transaction = await context.BankTransactions.SingleAsync(t => t.Id == id);
            transaction.CategoryId = categoryId;
        }
        else
        {
            var item = await context.AmazonOrderItems.SingleAsync(i => i.Id == id);
            item.CategoryId = categoryId;
        }
        await context.SaveChangesAsync();
    }

    public async Task<int> BulkCategorizeAsync(
        ExpenseDbContext context, IReadOnlyList<int> bankTransactionIds, IReadOnlyList<int> amazonItemIds, int categoryId)
    {
        var bankCount = bankTransactionIds.Count > 0
            ? await categorization.BulkCategorizeTransactionsAsync(context, bankTransactionIds, categoryId)
            : 0;
        var amazonCount = amazonItemIds.Count > 0
            ? await categorization.BulkCategorizeAmazonItemsAsync(context, amazonItemIds, categoryId)
            : 0;
        return bankCount + amazonCount;
    }

    public async Task UpdateAmazonItemDetailsAsync(ExpenseDbContext context, int itemId, string itemTitle, decimal price, int quantity)
    {
        var item = await context.AmazonOrderItems.SingleAsync(i => i.Id == itemId);
        item.ItemTitle = itemTitle;
        item.Price = price;
        item.Quantity = quantity;
        await context.SaveChangesAsync();
    }
}
