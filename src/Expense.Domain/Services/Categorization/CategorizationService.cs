using System.Text.RegularExpressions;
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
///
/// Also owns the Review Queue: querying pending rows, and categorizing one row while
/// optionally creating the merchant_rule/product that lets future imports match
/// automatically - "approved once, remembered forever." Creating that rule/product
/// also retroactively applies it to any other still-pending rows that match, so the
/// user never has to click through duplicates of something they just categorized.
/// </summary>
public class CategorizationService
{
    public async Task ApplyMerchantRuleAsync(ExpenseDbContext context, BankTransaction transaction)
    {
        if (transaction.IsAmazonMerchant) return;

        var searchText = (transaction.Merchant ?? transaction.Description).ToUpperInvariant();
        var rules = await context.MerchantRules.ToListAsync();

        var match = rules.FirstOrDefault(r => MerchantPatternMatcher.Matches(searchText, r.MerchantPattern));
        transaction.CategoryId = match?.CategoryId;
    }

    public async Task<List<BankTransaction>> GetPendingBankTransactionsAsync(ExpenseDbContext context) =>
        await context.BankTransactions
            .Where(t => t.CategoryId == null && !t.IsAmazonMerchant)
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync();

    public async Task<List<AmazonOrderItem>> GetPendingAmazonOrderItemsAsync(ExpenseDbContext context) =>
        await context.AmazonOrderItems
            .Where(i => i.ProductId == null)
            .OrderByDescending(i => i.OrderDate)
            .ToListAsync();

    /// <summary>
    /// Categorizes one transaction. If merchantPatternToCreate is given, also creates
    /// that merchant_rule and applies it to every other still-pending transaction that
    /// matches. Returns how many OTHER transactions were retroactively categorized.
    /// </summary>
    public async Task<int> CategorizeTransactionAsync(
        ExpenseDbContext context, int transactionId, int categoryId, string? merchantPatternToCreate)
    {
        var transaction = await context.BankTransactions.SingleAsync(t => t.Id == transactionId);
        transaction.CategoryId = categoryId;

        if (merchantPatternToCreate is null)
        {
            await context.SaveChangesAsync();
            return 0;
        }

        var rule = new MerchantRule { MerchantPattern = merchantPatternToCreate, CategoryId = categoryId };
        context.MerchantRules.Add(rule);
        await context.SaveChangesAsync();

        var otherPending = await GetPendingBankTransactionsAsync(context);
        var retroactiveCount = 0;
        foreach (var other in otherPending)
        {
            var searchText = (other.Merchant ?? other.Description).ToUpperInvariant();
            if (MerchantPatternMatcher.Matches(searchText, rule.MerchantPattern))
            {
                other.CategoryId = categoryId;
                retroactiveCount++;
            }
        }
        await context.SaveChangesAsync();
        return retroactiveCount;
    }

    /// <summary>
    /// Categorizes one Amazon order item. If productPatternToCreate is given, also
    /// creates that product and applies it to every other still-pending item that
    /// matches. Returns how many OTHER items were retroactively categorized.
    /// </summary>
    public async Task<int> CategorizeAmazonItemAsync(
        ExpenseDbContext context, int itemId, int categoryId, string? productPatternToCreate)
    {
        var item = await context.AmazonOrderItems.SingleAsync(i => i.Id == itemId);
        item.CategoryId = categoryId;

        if (productPatternToCreate is null)
        {
            await context.SaveChangesAsync();
            return 0;
        }

        var product = new Product { ProductPattern = productPatternToCreate, CategoryId = categoryId };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        item.ProductId = product.Id;

        var otherPending = await GetPendingAmazonOrderItemsAsync(context);
        var retroactiveCount = 0;
        foreach (var other in otherPending)
        {
            if (other.Id != item.Id && MerchantPatternMatcher.Matches(other.ItemTitle, product.ProductPattern))
            {
                other.ProductId = product.Id;
                other.CategoryId = categoryId;
                retroactiveCount++;
            }
        }
        await context.SaveChangesAsync();
        return retroactiveCount;
    }

    /// <summary>
    /// Sets the same category directly on every given transaction, regardless of what
    /// pattern (if any) they share - the Review Queue's multi-select bulk action. No
    /// merchant_rule is created, since a bulk selection may span multiple different
    /// merchants with no single common pattern to build one from.
    /// </summary>
    public async Task<int> BulkCategorizeTransactionsAsync(ExpenseDbContext context, IReadOnlyList<int> transactionIds, int categoryId)
    {
        var transactions = await context.BankTransactions.Where(t => transactionIds.Contains(t.Id)).ToListAsync();
        foreach (var transaction in transactions)
        {
            transaction.CategoryId = categoryId;
        }
        await context.SaveChangesAsync();
        return transactions.Count;
    }

    /// <summary>Same as BulkCategorizeTransactionsAsync, for Amazon items - no product is created either.</summary>
    public async Task<int> BulkCategorizeAmazonItemsAsync(ExpenseDbContext context, IReadOnlyList<int> itemIds, int categoryId)
    {
        var items = await context.AmazonOrderItems.Where(i => itemIds.Contains(i.Id)).ToListAsync();
        foreach (var item in items)
        {
            item.CategoryId = categoryId;
        }
        await context.SaveChangesAsync();
        return items.Count;
    }

    /// <summary>
    /// Re-checks every currently-pending transaction/item against all current
    /// merchant_rules/products, categorizing any that now match. Unlike the retroactive
    /// apply inside CategorizeTransactionAsync/CategorizeAmazonItemAsync (which only
    /// checks the one rule/product just created), this checks everything against
    /// everything - the safety net for rows a bug, or a rule created after they became
    /// pending, previously left stuck.
    /// </summary>
    public async Task<ReapplyRulesResult> ReapplyRulesToPendingAsync(ExpenseDbContext context)
    {
        var result = new ReapplyRulesResult();

        var pendingTransactions = await GetPendingBankTransactionsAsync(context);
        var rules = await context.MerchantRules.ToListAsync();
        foreach (var transaction in pendingTransactions)
        {
            var searchText = (transaction.Merchant ?? transaction.Description).ToUpperInvariant();
            var match = rules.FirstOrDefault(r => MerchantPatternMatcher.Matches(searchText, r.MerchantPattern));
            if (match is not null)
            {
                transaction.CategoryId = match.CategoryId;
                result.TransactionsUpdated++;
            }
        }

        var pendingItems = await GetPendingAmazonOrderItemsAsync(context);
        var products = await context.Products.ToListAsync();
        foreach (var item in pendingItems)
        {
            var match = products.FirstOrDefault(p => MerchantPatternMatcher.Matches(item.ItemTitle, p.ProductPattern));
            if (match is not null)
            {
                item.ProductId = match.Id;
                item.CategoryId = match.CategoryId;
                result.ItemsUpdated++;
            }
        }

        await context.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// Groups pending transactions by a derived merchant pattern so repeated merchants
    /// (Publix x15, Trader Joe's x8, etc.) resolve in one action instead of one row each.
    /// </summary>
    public async Task<List<PendingTransactionGroup>> GetPendingTransactionGroupsAsync(ExpenseDbContext context)
    {
        var pending = await GetPendingBankTransactionsAsync(context);
        return pending
            .GroupBy(t => DeriveMerchantPattern(t.Merchant ?? t.Description))
            .Select(g => new PendingTransactionGroup
            {
                SuggestedPattern = g.Key,
                SampleDescription = g.First().Description,
                SampleDate = g.First().TransactionDate,
                TransactionIds = g.Select(t => t.Id).ToList(),
                TotalAmount = g.Sum(t => t.Amount)
            })
            .OrderByDescending(g => g.TransactionIds.Count)
            .ToList();
    }

    /// <summary>Groups pending Amazon items by exact item title - real recurring products repeat verbatim.</summary>
    public async Task<List<PendingAmazonItemGroup>> GetPendingAmazonItemGroupsAsync(ExpenseDbContext context)
    {
        var pending = await GetPendingAmazonOrderItemsAsync(context);
        return pending
            .GroupBy(i => i.ItemTitle.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new PendingAmazonItemGroup
            {
                SuggestedPattern = g.Key,
                ItemTitle = g.Key,
                SampleDate = g.First().OrderDate,
                ItemIds = g.Select(i => i.Id).ToList(),
                TotalPrice = g.Sum(i => i.Price)
            })
            .OrderByDescending(g => g.ItemIds.Count)
            .ToList();
    }

    /// <summary>
    /// Derives a stable, Contains-safe merchant pattern from a raw bank description:
    /// collapses whitespace runs (real bank exports pad heavily), skips past Wells
    /// Fargo's "PURCHASE ... AUTHORIZED ON MM/DD" boilerplate when present (otherwise
    /// unrelated merchants all collapse into one useless group), then takes the leading
    /// run of non-digit words (up to 4) as the pattern - real reference numbers/dates/
    /// store numbers are numeric, real merchant names generally aren't.
    /// </summary>
    public static string DeriveMerchantPattern(string description)
    {
        var collapsed = Regex.Replace(description.ToUpperInvariant(), @"\s+", " ").Trim();

        var boilerplateMatch = Regex.Match(collapsed, @"AUTHORIZED ON \d{2}/\d{2}\s+(.*)");
        var region = boilerplateMatch.Success ? boilerplateMatch.Groups[1].Value : collapsed;

        var words = region.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var prefix = new List<string>();
        foreach (var word in words)
        {
            if (word.Any(char.IsDigit)) break;
            prefix.Add(word);
            if (prefix.Count == 4) break;
        }

        if (prefix.Count > 0) return string.Join(' ', prefix);
        return words.Length > 0 ? words[0] : collapsed;
    }
}
