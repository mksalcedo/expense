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
        if (match is not null)
        {
            transaction.CategoryId = match.CategoryId;
            return;
        }

        // No explicit rule - fall back to history: only auto-apply when every past
        // occurrence of this same derived merchant pattern agrees on one category, so a
        // merchant categorized differently before (genuinely ambiguous) is left pending
        // for Review Queue instead of silently guessing wrong.
        var historicalCategoryIds = await FindHistoricalCategoryIdsAsync(context, transaction.Merchant ?? transaction.Description);
        if (historicalCategoryIds.Count == 1)
        {
            transaction.CategoryId = historicalCategoryIds[0];
        }
    }

    /// <summary>
    /// Every distinct category ever used for a transaction whose derived merchant pattern
    /// matches the given description - "have I categorized something like this before,
    /// and what did I pick" - ordered by most recent occurrence of each category first.
    /// Matches in either direction (does the candidate's own pattern appear in this
    /// description, or does this description's pattern appear in the candidate) rather
    /// than requiring the two derived patterns to be exactly equal, since real
    /// descriptions for the same merchant vary in length between occurrences (e.g. a bare
    /// "IONOS" one month, "IONOS www.ionos.com PA" the next) - same reasoning as
    /// MerchantPatternMatcher's own Contains-based approach. Used both to auto-apply at
    /// import time (only when this returns exactly one category - see
    /// ApplyMerchantRuleAsync) and to pre-select a starting suggestion on Review Queue
    /// even when it doesn't.
    /// </summary>
    public async Task<List<int>> FindHistoricalCategoryIdsAsync(ExpenseDbContext context, string description)
    {
        var pattern = DeriveMerchantPattern(description);

        var categorized = await context.BankTransactions
            .Where(t => t.CategoryId != null && !t.IsAmazonMerchant)
            .OrderByDescending(t => t.TransactionDate)
            .Select(t => new { CategoryId = t.CategoryId!.Value, t.Merchant, t.Description })
            .ToListAsync();

        return categorized
            .Where(t =>
            {
                var candidateText = t.Merchant ?? t.Description;
                var candidatePattern = DeriveMerchantPattern(candidateText);
                return MerchantPatternMatcher.Matches(candidateText, pattern)
                    || MerchantPatternMatcher.Matches(description, candidatePattern);
            })
            .Select(t => t.CategoryId)
            .Distinct()
            .ToList();
    }

    public async Task<List<BankTransaction>> GetPendingBankTransactionsAsync(ExpenseDbContext context) =>
        await context.BankTransactions
            .Include(t => t.Account)
            .Where(t => t.CategoryId == null && !t.IsAmazonMerchant)
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync();

    // CategoryId, not ProductId, is the real "pending" signal - a bulk-categorized item
    // (see BulkCategorizeAmazonItemsAsync) deliberately only ever gets a CategoryId, never
    // a ProductId, so filtering on ProductId here left bulk-categorized items looking
    // pending forever even though they'd genuinely been categorized.
    public async Task<List<AmazonOrderItem>> GetPendingAmazonOrderItemsAsync(ExpenseDbContext context) =>
        await context.AmazonOrderItems
            .Where(i => i.CategoryId == null)
            .OrderByDescending(i => i.OrderDate)
            .ToListAsync();

    /// <summary>
    /// Finds the single NeedsReview item matching a scraped order's id - used by the
    /// clipboard-watcher staging flow (see docs/amazon-order-scraper-bookmarklet.md) to
    /// figure out which flagged item a background-detected scrape belongs to, using only
    /// the order id it carries rather than requiring a pre-selected row. Null once the item
    /// has already been corrected (NeedsReview false) - a stray repeat detection for the
    /// same order must not re-match and re-apply.
    /// </summary>
    public async Task<AmazonOrderItem?> FindNeedsReviewItemByOrderIdAsync(ExpenseDbContext context, string orderId) =>
        await context.AmazonOrderItems.FirstOrDefaultAsync(i => i.OrderId == orderId && i.NeedsReview);

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
    ///
    /// NeedsReview placeholders are never a source or target of that rule: their shared
    /// title is a parser fallback ("item details unavailable..."), not a real product
    /// name, so two NeedsReview items from unrelated orders can share it verbatim (e.g.
    /// two placeholders from the same multi-order digest email) - creating a rule from
    /// that text, or matching another still-unidentified item against it, would silently
    /// conflate two different real products (see GetPendingAmazonItemGroupsAsync, which
    /// already treats NeedsReview titles the same way for grouping).
    /// </summary>
    public async Task<int> CategorizeAmazonItemAsync(
        ExpenseDbContext context, int itemId, int categoryId, string? productPatternToCreate)
    {
        var item = await context.AmazonOrderItems.SingleAsync(i => i.Id == itemId);
        item.CategoryId = categoryId;

        if (productPatternToCreate is null || item.NeedsReview)
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
            if (other.Id != item.Id && !other.NeedsReview && MerchantPatternMatcher.Matches(other.ItemTitle, product.ProductPattern))
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
                continue;
            }

            // No explicit rule - same unanimous-history fallback as ApplyMerchantRuleAsync,
            // so a merchant that's only ever been categorized by hand still gets swept up
            // here instead of needing a rule created first.
            var historicalCategoryIds = await FindHistoricalCategoryIdsAsync(context, transaction.Merchant ?? transaction.Description);
            if (historicalCategoryIds.Count == 1)
            {
                transaction.CategoryId = historicalCategoryIds[0];
                result.TransactionsUpdated++;
            }
        }

        var pendingItems = await GetPendingAmazonOrderItemsAsync(context);
        var products = await context.Products.ToListAsync();
        foreach (var item in pendingItems.Where(i => !i.NeedsReview))
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
    /// Groups pending, non-dismissed transactions by a derived merchant pattern so repeated
    /// merchants (Publix x15, Trader Joe's x8, etc.) resolve in one action instead of one
    /// row each.
    /// </summary>
    public async Task<List<PendingTransactionGroup>> GetPendingTransactionGroupsAsync(ExpenseDbContext context)
    {
        var pending = (await GetPendingBankTransactionsAsync(context)).Where(t => !t.Dismissed);
        var groups = pending
            .GroupBy(t => DeriveMerchantPattern(t.Merchant ?? t.Description))
            .Select(g => new PendingTransactionGroup
            {
                SuggestedPattern = g.Key,
                SampleDescription = g.First().Description,
                SampleDate = g.First().TransactionDate,
                TransactionIds = g.Select(t => t.Id).ToList(),
                TotalAmount = g.Sum(t => t.Amount),
                AccountName = string.Join(", ", g.Select(t => t.Account.Name).Distinct())
            })
            .OrderByDescending(g => g.TransactionIds.Count)
            .ToList();

        foreach (var group in groups)
        {
            var historicalCategoryIds = await FindHistoricalCategoryIdsAsync(context, group.SampleDescription);
            group.SuggestedCategoryId = historicalCategoryIds.Count > 0 ? historicalCategoryIds[0] : null;
        }

        return groups;
    }

    /// <summary>
    /// Groups pending, non-dismissed Amazon items by exact item title - real recurring
    /// products repeat verbatim. NeedsReview items are the one exception: their shared
    /// placeholder title is a parser fallback, not a real product name, so grouping by it
    /// would silently combine unrelated orders into one misleading row (different real
    /// dates/amounts hidden behind one combined total) - each stays its own singleton group
    /// instead, carrying its own real order id so it can actually be tracked down.
    /// </summary>
    public async Task<List<PendingAmazonItemGroup>> GetPendingAmazonItemGroupsAsync(ExpenseDbContext context)
    {
        var pending = (await GetPendingAmazonOrderItemsAsync(context)).Where(i => !i.Dismissed).ToList();

        var needsReviewGroups = pending
            .Where(i => i.NeedsReview)
            .Select(i => new PendingAmazonItemGroup
            {
                SuggestedPattern = i.ItemTitle,
                ItemTitle = i.ItemTitle,
                SampleDate = i.OrderDate,
                ItemIds = [i.Id],
                TotalPrice = i.Price,
                TaxAllocated = i.TaxAllocated,
                NeedsReview = true,
                OrderId = i.OrderId,
                NeedsReviewReason = i.NeedsReviewReason,
                RawEmailBody = i.RawEmailBody,
                OrderDetailsUrl = i.OrderDetailsUrl
            });

        var groupedItems = pending
            .Where(i => !i.NeedsReview)
            .GroupBy(i => i.ItemTitle.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new PendingAmazonItemGroup
            {
                SuggestedPattern = g.Key,
                ItemTitle = g.Key,
                SampleDate = g.First().OrderDate,
                ItemIds = g.Select(i => i.Id).ToList(),
                TotalPrice = g.Sum(i => i.Price),
                TaxAllocated = g.Sum(i => i.TaxAllocated),
                // A group of exactly one real item unambiguously belongs to one order - safe
                // to expose here too (unlike a genuine multi-item group, which could span
                // several orders and has no single id to report). This is what keeps "Add
                // another item" on Review Queue working on a row even after its NeedsReview
                // placeholder has just been corrected into a real single-item row.
                OrderId = g.Count() == 1 ? g.Single().OrderId : null
            });

        return needsReviewGroups.Concat(groupedItems).OrderByDescending(g => g.ItemIds.Count).ToList();
    }

    /// <summary>Hides selected pending transactions from the Review Queue's action list without categorizing them - see BankTransaction.Dismissed.</summary>
    public async Task DismissTransactionsAsync(ExpenseDbContext context, IReadOnlyList<int> transactionIds)
    {
        var transactions = await context.BankTransactions.Where(t => transactionIds.Contains(t.Id)).ToListAsync();
        foreach (var transaction in transactions)
        {
            transaction.Dismissed = true;
        }
        await context.SaveChangesAsync();
    }

    /// <summary>Same as DismissTransactionsAsync, for Amazon items.</summary>
    public async Task DismissAmazonItemsAsync(ExpenseDbContext context, IReadOnlyList<int> itemIds)
    {
        var items = await context.AmazonOrderItems.Where(i => itemIds.Contains(i.Id)).ToListAsync();
        foreach (var item in items)
        {
            item.Dismissed = true;
        }
        await context.SaveChangesAsync();
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
