using System.Text.RegularExpressions;
using Expense.Domain.Entities;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Parses auto-confirm@amazon.com "Ordered: ..." plaintext email bodies. Tax/shipping
/// leftover (grand total minus the sum of item prices) is prorated across items
/// proportional to each item's price. Fails loudly (FormatException) rather than
/// returning empty/partial data when the body doesn't match the expected structure -
/// a missing order or item should never silently import as zero rows.
/// </summary>
public partial class AmazonOrderEmailParser
{
    [GeneratedRegex(@"Order #\s*\r?\n\s*(?<id>[\w-]+)")]
    private static partial Regex OrderIdPattern();

    [GeneratedRegex(@"\*\s*(?<title>.+?)\r?\n\s*Quantity:\s*(?<qty>\d+)\r?\n\s*(?<price>[\d.]+)\s*USD", RegexOptions.Singleline)]
    private static partial Regex ItemPattern();

    [GeneratedRegex(@"Grand Total:\s*\r?\n\s*(?<total>[\d.]+)\s*USD")]
    private static partial Regex GrandTotalPattern();

    public List<AmazonOrderItem> Parse(string emailBody, DateOnly orderDate)
    {
        var orderIdMatch = OrderIdPattern().Match(emailBody);
        if (!orderIdMatch.Success)
        {
            throw new FormatException("Could not find an 'Order #' line in the email body.");
        }
        var orderId = orderIdMatch.Groups["id"].Value.Trim();

        var itemMatches = ItemPattern().Matches(emailBody);
        if (itemMatches.Count == 0)
        {
            throw new FormatException($"Could not find any items in the email body for order {orderId}.");
        }

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
}
