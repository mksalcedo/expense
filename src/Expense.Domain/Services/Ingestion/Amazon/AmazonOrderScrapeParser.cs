using System.Text.Json;
using System.Text.Json.Serialization;

namespace Expense.Domain.Services.Ingestion.Amazon;

public record AmazonOrderScrapeItem(string Title, decimal Price, int Quantity);

public class AmazonOrderScrapePayload
{
    public string? OrderId { get; init; }
    public List<AmazonOrderScrapeItem> Items { get; init; } = [];
}

/// <summary>
/// Parses the JSON copied to the clipboard by the order-page bookmarklet (see
/// docs/amazon-order-scraper-bookmarklet.md) - item title/price/quantity scraped directly
/// from the Amazon order-details page's own DOM, instead of a screenshot for Claude Vision to
/// read. Exact, not probabilistic, and free of the vision-API round trip - but only as good
/// as the bookmarklet's selectors, which can't be verified without a live Amazon page in
/// front of a human. Static/pure so it's cheaply unit tested on its own.
/// </summary>
public static class AmazonOrderScrapeParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AmazonOrderScrapePayload? TryParse(string text)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<AmazonOrderScrapePayload>(text, JsonOptions);
            return payload is { Items.Count: > 0 } ? payload : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
