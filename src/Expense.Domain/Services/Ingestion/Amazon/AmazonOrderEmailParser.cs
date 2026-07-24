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

    // Matches either known "View or edit order"/"View or manage your orders" link path -
    // your-orders/order-details (newer template) or gp/css/order-details (older template)
    // both contain "order-details", so one pattern covers both without needing to special
    // case the surrounding label text, which also varies between templates.
    [GeneratedRegex(@"https://www\.amazon\.com/\S*order-details\S*")]
    private static partial Regex OrderDetailsLinkPattern();

    public List<AmazonOrderItem> Parse(string emailBody, DateOnly orderDate, string? messageId = null)
    {
        var orderIdMatch = OrderIdPattern().Match(emailBody);
        if (orderIdMatch.Success)
        {
            return ParseItemizedOrder(emailBody, orderDate, messageId);
        }

        var inlineIdMatches = InlineOrderIdPattern().Matches(emailBody);
        var orderTotalMatches = OrderTotalPattern().Matches(emailBody);
        if (inlineIdMatches.Count > 0 && orderTotalMatches.Count > 0)
        {
            return ParseSimplifiedOrders(emailBody, orderDate, inlineIdMatches, orderTotalMatches, messageId);
        }

        throw new FormatException("Could not find an 'Order #' line in the email body.");
    }

    private static List<AmazonOrderItem> ParseItemizedOrder(string emailBody, DateOnly orderDate, string? messageId)
    {
        var itemMatches = ItemPattern().Matches(emailBody);

        if (itemMatches.Count == 0)
        {
            // A third real template: multi-line "Order #" (unlike the inline simplified
            // template below) but still no item list at all, just a Grand Total per
            // order. Some real notifications bundle multiple distinct orders in one
            // message this way (a Gmail "digest") - every Order#/Grand Total pair found
            // gets its own placeholder, not just the first, since dropping the rest
            // silently would understate real spending the moment one has a nonzero total.
            return BuildPlaceholderOrdersForBundledEmail(emailBody, orderDate, messageId);
        }

        var orderId = OrderIdPattern().Match(emailBody).Groups["id"].Value.Trim();
        var grandTotalMatch = GrandTotalPattern().Match(emailBody);
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

    // Pairs order ids with grand totals/links purely by their sequential position in the
    // body (1st order with the 1st total/link, 2nd with the 2nd, ...) rather than by
    // proximity/regex span math - every real bundled-order email seen has them
    // alternating in that exact order, and this is simpler than slicing the body into
    // per-order text segments.
    private static List<AmazonOrderItem> ParseSimplifiedOrders(
        string emailBody, DateOnly orderDate, MatchCollection orderIdMatches, MatchCollection orderTotalMatches, string? messageId)
    {
        if (orderIdMatches.Count != orderTotalMatches.Count)
        {
            throw new FormatException(
                $"Found {orderIdMatches.Count} order id(s) but {orderTotalMatches.Count} order total(s) in the email body - can't reliably pair them.");
        }

        var linkMatches = OrderDetailsLinkPattern().Matches(emailBody);
        var reason = BuildReason(orderIdMatches.Count);
        var results = new List<AmazonOrderItem>();
        for (var i = 0; i < orderIdMatches.Count; i++)
        {
            var orderId = orderIdMatches[i].Groups["id"].Value.Trim();
            var orderTotal = decimal.Parse(orderTotalMatches[i].Groups["total"].Value);
            var orderDetailsUrl = ExtractOrderDetailsUrl(linkMatches, i, orderIdMatches.Count, orderId);
            results.AddRange(BuildPlaceholderOrder(emailBody, orderDate, orderId, orderTotal, messageId, reason, orderDetailsUrl));
        }
        return results;
    }

    private static List<AmazonOrderItem> BuildPlaceholderOrdersForBundledEmail(string emailBody, DateOnly orderDate, string? messageId)
    {
        var orderIdMatches = OrderIdPattern().Matches(emailBody);
        var grandTotalMatches = GrandTotalPattern().Matches(emailBody);

        if (grandTotalMatches.Count == 0)
        {
            throw new FormatException("Could not find any items in the email body.");
        }
        if (orderIdMatches.Count != grandTotalMatches.Count)
        {
            throw new FormatException(
                $"Found {orderIdMatches.Count} order(s) but {grandTotalMatches.Count} grand total(s) in the email body - can't reliably pair them.");
        }

        var linkMatches = OrderDetailsLinkPattern().Matches(emailBody);
        var reason = BuildReason(orderIdMatches.Count);
        var results = new List<AmazonOrderItem>();
        for (var i = 0; i < orderIdMatches.Count; i++)
        {
            var orderId = orderIdMatches[i].Groups["id"].Value.Trim();
            var orderTotal = decimal.Parse(grandTotalMatches[i].Groups["total"].Value);
            var orderDetailsUrl = ExtractOrderDetailsUrl(linkMatches, i, orderIdMatches.Count, orderId);
            results.AddRange(BuildPlaceholderOrder(emailBody, orderDate, orderId, orderTotal, messageId, reason, orderDetailsUrl));
        }
        return results;
    }

    // "Bundled with N other order(s)..." only applies when there's genuinely more than one
    // order in the body - the common case (a single no-detail order) gets the plainer,
    // more accurate reason instead.
    private static string BuildReason(int totalOrdersInEmail) =>
        totalOrdersInEmail > 1
            ? $"Bundled with {totalOrdersInEmail - 1} other order(s) in one digest email"
            : "No item detail in confirmation email";

    // Links pair positionally with orders exactly like grand totals do, but less
    // reliably (some templates omit the link, or a body-wide unrelated link could throw
    // the count off) - only trust the positional pairing when the counts line up exactly;
    // otherwise fall back to a constructed link built from the order id we already parsed
    // correctly, so there's always something clickable rather than a mismatched link.
    private static string ExtractOrderDetailsUrl(MatchCollection linkMatches, int index, int expectedCount, string orderId) =>
        linkMatches.Count == expectedCount ? linkMatches[index].Value.Trim() : BuildFallbackOrderDetailsUrl(orderId);

    private static string BuildFallbackOrderDetailsUrl(string orderId) =>
        $"https://www.amazon.com/gp/css/order-details?orderId={orderId}";

    private static List<AmazonOrderItem> BuildPlaceholderOrder(
        string emailBody, DateOnly orderDate, string orderId, decimal orderTotal, string? messageId, string? reason, string? orderDetailsUrl)
    {
        var isGiftCard = emailBody.Contains("gift card", StringComparison.OrdinalIgnoreCase);
        var title = isGiftCard
            ? "Amazon eGift Card"
            : "(Item details unavailable in email - check Amazon order page)";
        var needsReview = !isGiftCard;

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
                NeedsReview = needsReview,
                CreatedAt = DateTimeOffset.UtcNow,
                SourceMessageId = needsReview ? messageId : null,
                RawEmailBody = needsReview ? emailBody : null,
                NeedsReviewReason = needsReview ? reason : null,
                OrderDetailsUrl = needsReview ? orderDetailsUrl : null
            }
        ];
    }
}
