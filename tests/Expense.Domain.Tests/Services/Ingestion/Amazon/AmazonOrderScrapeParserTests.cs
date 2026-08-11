using Expense.Domain.Services.Ingestion.Amazon;

namespace Expense.Domain.Tests.Services.Ingestion.Amazon;

public class AmazonOrderScrapeParserTests
{
    [Fact]
    public void TryParse_ParsesAWellFormedPayload_WithOrderIdAndMultipleItems()
    {
        const string json = """
            {
              "orderId": "113-0140431-5777821",
              "items": [
                { "title": "THORNE Vitamin C", "price": 24.99, "quantity": 1 },
                { "title": "NeoCell Grassfed Collagen Peptides Powder", "price": 32.50, "quantity": 2 }
              ]
            }
            """;

        var payload = AmazonOrderScrapeParser.TryParse(json);

        Assert.NotNull(payload);
        Assert.Equal("113-0140431-5777821", payload.OrderId);
        Assert.Equal(2, payload.Items.Count);
        Assert.Equal("THORNE Vitamin C", payload.Items[0].Title);
        Assert.Equal(24.99m, payload.Items[0].Price);
        Assert.Equal(1, payload.Items[0].Quantity);
        Assert.Equal("NeoCell Grassfed Collagen Peptides Powder", payload.Items[1].Title);
        Assert.Equal(32.50m, payload.Items[1].Price);
        Assert.Equal(2, payload.Items[1].Quantity);
    }

    [Fact]
    public void TryParse_AllowsAMissingOrderId()
    {
        const string json = """{"items": [{ "title": "THORNE Vitamin C", "price": 24.99, "quantity": 1 }]}""";

        var payload = AmazonOrderScrapeParser.TryParse(json);

        Assert.NotNull(payload);
        Assert.Null(payload.OrderId);
        Assert.Single(payload.Items);
    }

    [Fact]
    public void TryParse_ReturnsNull_ForGarbageText()
    {
        var payload = AmazonOrderScrapeParser.TryParse("this is not json at all");

        Assert.Null(payload);
    }

    [Fact]
    public void TryParse_ReturnsNull_ForAnEmptyItemsArray()
    {
        var payload = AmazonOrderScrapeParser.TryParse("""{"items": []}""");

        Assert.Null(payload);
    }

    [Fact]
    public void TryParse_ReturnsNull_ForValidJsonMissingTheItemsField()
    {
        var payload = AmazonOrderScrapeParser.TryParse("""{"orderId": "123"}""");

        Assert.Null(payload);
    }

    [Fact]
    public void TryParse_ReturnsNull_ForAPlainScreenshotStyleJsonArray()
    {
        // A pasted screenshot never reaches this parser (routed separately by media type),
        // but if someone pastes the old plain-array text shape by mistake, it must fail
        // clearly rather than partially matching.
        var payload = AmazonOrderScrapeParser.TryParse("""["THORNE Vitamin C"]""");

        Assert.Null(payload);
    }
}
