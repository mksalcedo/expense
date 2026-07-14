using System.Text.RegularExpressions;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Parses payments-messages@amazon.com refund confirmation emails - scoped to this one
/// clean, well-structured template. Real Amazon refund emails come in several other
/// templates (advance refund issued, dropoff confirmed, return request confirmed) with
/// different wording that this parser does not handle; feeding it one of those should
/// fail loudly rather than silently return wrong/empty data.
/// </summary>
public partial class AmazonRefundEmailParser
{
    [GeneratedRegex(@"for your Order (?<id>[\w-]+)")]
    private static partial Regex OrderIdPattern();

    // Either an itemized "Item Refund: $X" (+ optional "Item Tax Refund: $Y"), or a
    // single combined "Goodwill Refund: $X" line - both real variants seen in practice.
    [GeneratedRegex(
        @"Item:\s*(?<title>.+?)\s+Quantity:\s*(?<qty>\d+).*?(?:Item Refund:\s*\$(?<itemRefund>[\d.]+)(?:\s+Item Tax Refund:\s*\$(?<taxRefund>[\d.]+))?|Goodwill Refund:\s*\$(?<goodwill>[\d.]+))",
        RegexOptions.Singleline)]
    private static partial Regex ItemRefundPattern();

    public List<AmazonRefundInfo> Parse(string emailBody)
    {
        var orderIdMatch = OrderIdPattern().Match(emailBody);
        if (!orderIdMatch.Success)
        {
            throw new FormatException("Could not find an 'Order' reference in the refund email body.");
        }
        var orderId = orderIdMatch.Groups["id"].Value.Trim();

        var itemMatches = ItemRefundPattern().Matches(emailBody);
        if (itemMatches.Count == 0)
        {
            throw new FormatException($"Could not find any refunded item detail in the email body for order {orderId}.");
        }

        return itemMatches.Select(m =>
        {
            var itemRefund = m.Groups["itemRefund"].Success ? decimal.Parse(m.Groups["itemRefund"].Value) : 0m;
            var taxRefund = m.Groups["taxRefund"].Success ? decimal.Parse(m.Groups["taxRefund"].Value) : 0m;
            var goodwill = m.Groups["goodwill"].Success ? decimal.Parse(m.Groups["goodwill"].Value) : 0m;

            return new AmazonRefundInfo
            {
                OrderId = orderId,
                ItemTitle = m.Groups["title"].Value.Trim(),
                Quantity = int.Parse(m.Groups["qty"].Value),
                RefundAmount = itemRefund + taxRefund + goodwill
            };
        }).ToList();
    }
}
