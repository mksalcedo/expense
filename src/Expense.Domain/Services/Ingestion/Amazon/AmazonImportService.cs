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
            var exists = await context.AmazonOrderItems
                .AnyAsync(i => i.OrderId == item.OrderId && i.ItemTitle == item.ItemTitle, cancellationToken);
            if (exists)
            {
                summary.DuplicatesSkipped++;
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
        }

        await context.SaveChangesAsync(cancellationToken);
        return summary;
    }
}
