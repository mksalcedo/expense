using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Services.Ingestion.ManualCharges;

namespace Expense.Domain.Tests.Services.Ingestion.Amazon;

public class AmazonOrderScreenshotParsingServiceTests
{
    private class FakeAnthropicVisionClient(string responseText) : IAnthropicVisionClient
    {
        public byte[]? LastImageBytes { get; private set; }
        public string? LastMediaType { get; private set; }
        public string? LastPrompt { get; private set; }

        public Task<string> SendImagePromptAsync(byte[] imageBytes, string mediaType, string prompt, CancellationToken cancellationToken = default)
        {
            LastImageBytes = imageBytes;
            LastMediaType = mediaType;
            LastPrompt = prompt;
            return Task.FromResult(responseText);
        }
    }

    [Fact]
    public async Task ParseScreenshotAsync_ParsesAPlainJsonArrayOfTitles()
    {
        const string response = """["THORNE Vitamin C", "NeoCell Grassfed Collagen Peptides Powder"]""";
        var client = new FakeAnthropicVisionClient(response);
        var sut = new AmazonOrderScreenshotParsingService(client);

        var titles = await sut.ParseScreenshotAsync([1, 2, 3], "image/png");

        Assert.Equal(2, titles.Count);
        Assert.Equal("THORNE Vitamin C", titles[0]);
        Assert.Equal("NeoCell Grassfed Collagen Peptides Powder", titles[1]);
    }

    [Fact]
    public async Task ParseScreenshotAsync_ReturnsASingleTitle_ForAnOrdinaryOneItemOrder()
    {
        var client = new FakeAnthropicVisionClient("""["Pure Encapsulations NAC 600 mg"]""");
        var sut = new AmazonOrderScreenshotParsingService(client);

        var titles = await sut.ParseScreenshotAsync([1], "image/png");

        var title = Assert.Single(titles);
        Assert.Equal("Pure Encapsulations NAC 600 mg", title);
    }

    [Fact]
    public async Task ParseScreenshotAsync_StripsMarkdownCodeFences_IfPresent()
    {
        const string response = """
            Here are the items I found:
            ```json
            ["THORNE Vitamin C"]
            ```
            """;
        var client = new FakeAnthropicVisionClient(response);
        var sut = new AmazonOrderScreenshotParsingService(client);

        var titles = await sut.ParseScreenshotAsync([1], "image/png");

        var title = Assert.Single(titles);
        Assert.Equal("THORNE Vitamin C", title);
    }

    [Fact]
    public async Task ParseScreenshotAsync_ReturnsEmptyList_WhenNoItemsFound()
    {
        var client = new FakeAnthropicVisionClient("[]");
        var sut = new AmazonOrderScreenshotParsingService(client);

        var titles = await sut.ParseScreenshotAsync([1], "image/png");

        Assert.Empty(titles);
    }

    [Fact]
    public async Task ParseScreenshotAsync_PassesTheImageBytesAndMediaTypeThrough()
    {
        var client = new FakeAnthropicVisionClient("[]");
        var sut = new AmazonOrderScreenshotParsingService(client);
        byte[] imageBytes = [9, 8, 7];

        await sut.ParseScreenshotAsync(imageBytes, "image/jpeg");

        Assert.Equal(imageBytes, client.LastImageBytes);
        Assert.Equal("image/jpeg", client.LastMediaType);
    }

    [Fact]
    public void BuildPrompt_MentionsAmazonOrderDetails()
    {
        var prompt = AmazonOrderScreenshotParsingService.BuildPrompt();

        Assert.Contains("Amazon", prompt);
        Assert.Contains("order", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
