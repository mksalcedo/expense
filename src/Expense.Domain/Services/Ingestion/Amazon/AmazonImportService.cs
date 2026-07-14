using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Ties AmazonOrderEmailParser/AmazonRefundEmailParser into real import runs.
/// Dedup is by (order_id, item_title) directly - Amazon already supplies a real
/// unique order ID, unlike bank transactions (see DedupService's own note on this).
/// Product matching mirrors CategorizationService's merchant-rule matching, just
/// against products instead of merchant_rules; no match leaves the item pending
/// categorization (product_id/category_id both null).
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

            var match = products.FirstOrDefault(p => Matches(item.ItemTitle, p.ProductPattern));
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
        ExpenseDbContext context, string emailBody, CancellationToken cancellationToken = default)
    {
        var refunds = refundParser.Parse(emailBody);
        var summary = new AmazonImportSummary();

        foreach (var refund in refunds)
        {
            var existing = await context.AmazonOrderItems
                .SingleOrDefaultAsync(i => i.OrderId == refund.OrderId && i.ItemTitle == refund.ItemTitle, cancellationToken);
            if (existing is null)
            {
                summary.UnmatchedRefunds.Add($"{refund.OrderId}: {refund.ItemTitle}");
                continue;
            }

            existing.RefundAmount = refund.RefundAmount;
            summary.RefundsApplied++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return summary;
    }

    private static bool Matches(string text, string pattern) =>
        text.Contains(pattern.Trim('%'), StringComparison.OrdinalIgnoreCase);
}
