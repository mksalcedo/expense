using Expense.Domain.Services.Ingestion.ManualCharges;

namespace Expense.Domain.Tests.Services.Ingestion.ManualCharges;

public class AmexScreenshotParsingServiceTests
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
    public async Task ParseScreenshotAsync_ParsesAPlainJsonArrayResponse()
    {
        const string response = """
            [
                {"date": "2026-07-20", "description": "MORGAN COMPOUDING", "amount": 131.65, "isCredit": false},
                {"date": "2026-07-20", "description": "PUBLIX", "amount": 153.77, "isCredit": false}
            ]
            """;
        var client = new FakeAnthropicVisionClient(response);
        var sut = new AmexScreenshotParsingService(client);

        var rows = await sut.ParseScreenshotAsync([1, 2, 3], "image/png", new DateOnly(2026, 7, 21));

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 7, 20), rows[0].Date);
        Assert.Equal("MORGAN COMPOUDING", rows[0].Description);
        Assert.Equal(131.65m, rows[0].Amount);
        Assert.False(rows[0].IsCredit);
    }

    [Fact]
    public async Task ParseScreenshotAsync_ParsesACreditRow()
    {
        const string response = """[{"date": "2026-07-20", "description": "ONLINE PAYMENT - THANK YOU", "amount": 1000.00, "isCredit": true}]""";
        var client = new FakeAnthropicVisionClient(response);
        var sut = new AmexScreenshotParsingService(client);

        var rows = await sut.ParseScreenshotAsync([1], "image/png", new DateOnly(2026, 7, 21));

        var row = Assert.Single(rows);
        Assert.True(row.IsCredit);
        Assert.Equal(1000.00m, row.Amount);
    }

    [Fact]
    public async Task ParseScreenshotAsync_StripsMarkdownCodeFences_IfPresent()
    {
        const string response = """
            Here is the extracted data:
            ```json
            [{"date": "2026-07-20", "description": "PUBLIX", "amount": 153.77, "isCredit": false}]
            ```
            """;
        var client = new FakeAnthropicVisionClient(response);
        var sut = new AmexScreenshotParsingService(client);

        var rows = await sut.ParseScreenshotAsync([1], "image/png", new DateOnly(2026, 7, 21));

        var row = Assert.Single(rows);
        Assert.Equal("PUBLIX", row.Description);
    }

    [Fact]
    public async Task ParseScreenshotAsync_ReturnsEmptyList_WhenNoTransactionsFound()
    {
        var client = new FakeAnthropicVisionClient("[]");
        var sut = new AmexScreenshotParsingService(client);

        var rows = await sut.ParseScreenshotAsync([1], "image/png", new DateOnly(2026, 7, 21));

        Assert.Empty(rows);
    }

    [Fact]
    public async Task ParseScreenshotAsync_PassesTheImageBytesAndMediaTypeThrough()
    {
        var client = new FakeAnthropicVisionClient("[]");
        var sut = new AmexScreenshotParsingService(client);
        byte[] imageBytes = [9, 8, 7];

        await sut.ParseScreenshotAsync(imageBytes, "image/jpeg", new DateOnly(2026, 7, 21));

        Assert.Equal(imageBytes, client.LastImageBytes);
        Assert.Equal("image/jpeg", client.LastMediaType);
    }

    [Fact]
    public void BuildPrompt_MentionsTheAsOfDate_ForYearInference()
    {
        var prompt = AmexScreenshotParsingService.BuildPrompt(new DateOnly(2026, 7, 21));

        Assert.Contains("2026-07-21", prompt);
    }
}
