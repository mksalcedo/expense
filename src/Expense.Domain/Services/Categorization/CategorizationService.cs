using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Categorization;

/// <summary>
/// Applies merchant rules at import time. Amazon-merchant transactions are always
/// skipped - their category lives entirely at the amazon_order_items level, never
/// here (the Amex forecast never needs a transaction-level category at all, only the
/// Spending Tracker does, and it reads Amazon detail from a different table).
/// A transaction that matches no rule is left with CategoryId == null, which is the
/// entire "pending categorization" state - no separate status column.
/// </summary>
public class CategorizationService
{
    public async Task ApplyMerchantRuleAsync(ExpenseDbContext context, BankTransaction transaction)
    {
        if (transaction.IsAmazonMerchant) return;

        var searchText = (transaction.Merchant ?? transaction.Description).ToUpperInvariant();
        var rules = await context.MerchantRules.ToListAsync();

        var match = rules.FirstOrDefault(r => Matches(searchText, r.MerchantPattern));
        transaction.CategoryId = match?.CategoryId;
    }

    private static bool Matches(string text, string pattern) =>
        text.Contains(pattern.Trim('%'), StringComparison.OrdinalIgnoreCase);
}
