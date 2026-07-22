using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Ties AmazonOrderEmailParser/AmazonRefundEmailParser into real import runs.
/// Order dedup is by (order_id, item_title) directly - Amazon already supplies a real
/// unique order ID, unlike bank transactions (see DedupService's own note on this).
/// Product matching mirrors CategorizationService's merchant-rule matching, just
/// against products instead of merchant_rules; no match leaves the item pending
/// categorization (product_id/category_id both null).
///
/// A refund is imported as its own new, independently-categorized negative-amount row
/// (same product-match-or-pending treatment as a regular order item) rather than being
/// matched back to the original purchase row - per design-summary.md, a refund is just
/// a transaction categorized the same way the original charge would be, nothing more.
/// Dedup is by (order_id, item_title, price) since the negative price never collides
/// with the original purchase's own positive-price row under the same order/item key.
/// </summary>
public class AmazonImportService(AmazonOrderEmailParser orderParser, AmazonRefundEmailParser refundParser)
{
    public async Task<AmazonImportSummary> ImportOrderAsync(
        ExpenseDbContext context, string emailBody, DateOnly orderDate, CancellationToken cancellationToken = default)
    {
        var items = orderParser.Parse(emailBody, orderDate);
        var summary = new AmazonImportSummary();
        var products = await context.Products.ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            // A placeholder item (NeedsReview - see AmazonOrderEmailParser.BuildPlaceholderOrder)
            // is always the sole representative of its whole order, and its title is expected
            // to be replaced by hand once the user checks the real order page. Deduping it by
            // (OrderId, ItemTitle) like a normal item would break the moment that edit happens -
            // a later re-scan of the same email always regenerates the original placeholder
            // text, which no longer matches the user's corrected title, so it looked like a
            // brand new item and got inserted as a duplicate (a real bug found in production).
            // OrderId alone is a safe, stable key here specifically because there's structurally
            // only ever one such row per order.
            var exists = item.NeedsReview
                ? await context.AmazonOrderItems.AnyAsync(i => i.OrderId == item.OrderId, cancellationToken)
                : await context.AmazonOrderItems.AnyAsync(i => i.OrderId == item.OrderId && i.ItemTitle == item.ItemTitle, cancellationToken);
            if (exists)
            {
                summary.DuplicatesSkipped++;
                summary.ItemOutcomes.Add(new AmazonItemOutcome(item.ItemTitle, item.Price, item.Quantity, WasDuplicate: true, NeedsReview: item.NeedsReview));
                continue;
            }

            var match = products.FirstOrDefault(p => MerchantPatternMatcher.Matches(item.ItemTitle, p.ProductPattern));
            if (match is not null)
            {
                item.ProductId = match.Id;
                item.CategoryId = match.CategoryId;
            }

            context.AmazonOrderItems.Add(item);
            summary.ItemsAdded++;
            summary.ItemOutcomes.Add(new AmazonItemOutcome(item.ItemTitle, item.Price, item.Quantity, WasDuplicate: false, NeedsReview: item.NeedsReview));
        }

        await context.SaveChangesAsync(cancellationToken);
        return summary;
    }

    public async Task<AmazonImportSummary> ImportRefundAsync(
        ExpenseDbContext context, string emailBody, DateOnly receivedDate, CancellationToken cancellationToken = default)
    {
        var refunds = refundParser.Parse(emailBody);
        var summary = new AmazonImportSummary();
        var products = await context.Products.ToListAsync(cancellationToken);

        foreach (var refund in refunds)
        {
            var refundPrice = -refund.RefundAmount;

            // Dedup on (order, item, this exact refund amount) rather than the original
            // purchase's own (order, item) key - a refund is its own row now, and a
            // negative price never collides with the positive-price purchase row.
            var exists = await context.AmazonOrderItems
                .AnyAsync(i => i.OrderId == refund.OrderId && i.ItemTitle == refund.ItemTitle && i.Price == refundPrice, cancellationToken);
            if (exists)
            {
                summary.RefundDuplicatesSkipped++;
                summary.ItemOutcomes.Add(new AmazonItemOutcome(refund.ItemTitle, refundPrice, 1, WasDuplicate: true));
                continue;
            }

            var match = products.FirstOrDefault(p => MerchantPatternMatcher.Matches(refund.ItemTitle, p.ProductPattern));

            context.AmazonOrderItems.Add(new AmazonOrderItem
            {
                OrderId = refund.OrderId,
                OrderDate = receivedDate,
                ItemTitle = refund.ItemTitle,
                Price = refundPrice,
                Quantity = 1, // the refund amount already covers however many units were refunded - never multiply it again
                ProductId = match?.Id,
                CategoryId = match?.CategoryId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            summary.RefundsApplied++;
            summary.ItemOutcomes.Add(new AmazonItemOutcome(refund.ItemTitle, refundPrice, 1, WasDuplicate: false));
        }

        await context.SaveChangesAsync(cancellationToken);
        return summary;
    }

    /// <summary>
    /// Adds a single item the user typed in by hand - e.g. resolving a SyncIssue whose
    /// email never parsed into a real order at all. Same product-match-or-pending
    /// treatment as a normal import; no dedup check, since this is a deliberate one-off
    /// action rather than a re-scannable sync.
    /// </summary>
    public async Task<AmazonOrderItem> AddManualItemAsync(
        ExpenseDbContext context, string orderId, DateOnly orderDate, string itemTitle, decimal price, int quantity,
        CancellationToken cancellationToken = default)
    {
        var products = await context.Products.ToListAsync(cancellationToken);
        var match = products.FirstOrDefault(p => MerchantPatternMatcher.Matches(itemTitle, p.ProductPattern));

        var item = new AmazonOrderItem
        {
            OrderId = orderId,
            OrderDate = orderDate,
            ItemTitle = itemTitle,
            Price = price,
            Quantity = quantity,
            ProductId = match?.Id,
            CategoryId = match?.CategoryId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.AmazonOrderItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);
        return item;
    }
}
