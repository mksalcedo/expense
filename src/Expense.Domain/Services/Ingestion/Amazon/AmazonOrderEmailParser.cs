using System.Text.RegularExpressions;
using Expense.Domain.Entities;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Parses auto-confirm@amazon.com "Ordered: ..." plaintext email bodies. Tax/shipping
/// leftover (grand total minus the sum of item prices) is prorated across items
/// proportional to each item's price. Fails loudly (FormatException) rather than
/// returning empty/partial data when the body doesn't match the expected structure -
/// a missing order or item should never silently import as zero rows.
///
/// Also handles two further real templates that omit the item list entirely, just an
/// order total: a "simplified" one (inline "Order #<id>", "Order Total: $X") used for
/// gift cards and some real item orders, and a multi-line-"Order #" one ("Grand Total:")
/// used for some real orders too - both fall back to a single placeholder item for the
/// full total rather than throwing. Gift cards get a recognizable title so they
/// naturally match a "%GIFT CARD%" product pattern (e.g. routed to Off-Budget/Misc);
/// other no-detail orders get a placeholder title and NeedsReview=true so the dollar
/// amount still lands in the normal pending-categorization queue instead of being
/// silently dropped.
/// </summary>
public partial class AmazonOrderEmailParser
{
    [GeneratedRegex(@"Order #\s*\r?\n\s*(?<id>[\w-]+)")]
    private static partial Regex OrderIdPattern();

    [GeneratedRegex(@"\*\s*(?<title>.+?)\r?\n\s*Quantity:\s*(?<qty>\d+)\r?\n\s*(?<price>[\d.]+)\s*USD", RegexOptions.Singleline)]
    private static partial Regex ItemPattern();

    // Recent emails say "Grand Total:", older ones just say "Total" with no colon
    [GeneratedRegex(@"(?:Grand )?Total:?\s*\r?\n\s*(?<total>[\d.]+)\s*USD")]
    private static partial Regex GrandTotalPattern();

    [GeneratedRegex(@"Order #(?<id>[\w-]+)")]
    private static partial Regex InlineOrderIdPattern();

    [GeneratedRegex(@"Order Total:\s*\$(?<total>[\d.]+)")]
    private static partial Regex OrderTotalPattern();

    public List<AmazonOrderItem> Parse(string emailBody, DateOnly orderDate)
    {
        var orderIdMatch = OrderIdPattern().Match(emailBody);
        if (orderIdMatch.Success)
        {
            return ParseItemizedOrder(emailBody, orderDate, orderIdMatch.Groups["id"].Value.Trim());
        }

        var inlineIdMatch = InlineOrderIdPattern().Match(emailBody);
        var orderTotalMatch = OrderTotalPattern().Match(emailBody);
        if (inlineIdMatch.Success && orderTotalMatch.Success)
        {
            return ParseSimplifiedOrder(
                emailBody, orderDate, inlineIdMatch.Groups["id"].Value.Trim(), decimal.Parse(orderTotalMatch.Groups["total"].Value));
        }

        throw new FormatException("Could not find an 'Order #' line in the email body.");
    }

    private static List<AmazonOrderItem> ParseItemizedOrder(string emailBody, DateOnly orderDate, string orderId)
    {
        var itemMatches = ItemPattern().Matches(emailBody);
        var grandTotalMatch = GrandTotalPattern().Match(emailBody);

        if (itemMatches.Count == 0)
        {
            // A third real template: multi-line "Order #" (unlike the inline simplified
            // template below) but still no item list at all, just a Grand Total - falls
            // back the same way the simplified template does, rather than throwing, since
            // we at least have a real dollar amount to record.
            if (!grandTotalMatch.Success)
            {
                throw new FormatException($"Could not find any items in the email body for order {orderId}.");
            }
            return BuildPlaceholderOrder(emailBody, orderDate, orderId, decimal.Parse(grandTotalMatch.Groups["total"].Value));
        }

        if (!grandTotalMatch.Success)
        {
            throw new FormatException($"Could not find a 'Grand Total' in the email body for order {orderId}.");
        }
        var grandTotal = decimal.Parse(grandTotalMatch.Groups["total"].Value);

        var parsedItems = itemMatches
            .Select(m => (
                Title: m.Groups["title"].Value.Trim(),
                Quantity: int.Parse(m.Groups["qty"].Value),
                Price: decimal.Parse(m.Groups["price"].Value)))
            .ToList();

        var itemPriceSum = parsedItems.Sum(i => i.Price);
        var taxLeftover = grandTotal - itemPriceSum;

        return parsedItems.Select(i => new AmazonOrderItem
        {
            OrderId = orderId,
            OrderDate = orderDate,
            ItemTitle = i.Title,
            Price = i.Price,
            Quantity = i.Quantity,
            TaxAllocated = itemPriceSum == 0 ? 0 : Math.Round(taxLeftover * (i.Price / itemPriceSum), 2),
            CreatedAt = DateTimeOffset.UtcNow
        }).ToList();
    }

    private static List<AmazonOrderItem> ParseSimplifiedOrder(string emailBody, DateOnly orderDate, string orderId, decimal orderTotal) =>
        BuildPlaceholderOrder(emailBody, orderDate, orderId, orderTotal);

    private static List<AmazonOrderItem> BuildPlaceholderOrder(string emailBody, DateOnly orderDate, string orderId, decimal orderTotal)
    {
        var isGiftCard = emailBody.Contains("gift card", StringComparison.OrdinalIgnoreCase);
        var title = isGiftCard
            ? "Amazon eGift Card"
            : "(Item details unavailable in email - check Amazon order page)";

        return
        [
            new AmazonOrderItem
            {
                OrderId = orderId,
                OrderDate = orderDate,
                ItemTitle = title,
                Price = orderTotal,
                Quantity = 1,
                TaxAllocated = 0m,
                NeedsReview = !isGiftCard,
                CreatedAt = DateTimeOffset.UtcNow
            }
        ];
    }
}
