using System.Text.Json;
using System.Text.Json.Serialization;

namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>
/// Builds the extraction prompt and parses the response for a single screenshot of a card
/// issuer's transaction list - see docs/amex-pending-charges-plan.md. The actual API call is
/// delegated to IAnthropicVisionClient so this orchestration/parsing logic can be unit tested
/// without a live call.
/// </summary>
public class AmexScreenshotParsingService(IAnthropicVisionClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<ExtractedChargeRow>> ParseScreenshotAsync(
        byte[] imageBytes, string mediaType, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var responseText = await client.SendImagePromptAsync(imageBytes, mediaType, BuildPrompt(asOfDate), cancellationToken);
        return ParseResponse(responseText);
    }

    public static string BuildPrompt(DateOnly asOfDate) => $"""
        You are extracting transaction data from a screenshot of a credit card's "recent activity" or transaction list webpage.

        Today's date is {asOfDate:yyyy-MM-dd}. Dates in the screenshot may omit the year (e.g. "Jul 20") - infer the
        correct year assuming every transaction is recent (on or before today, never in the future).

        For each transaction row visible in the screenshot, extract:
        - date: the transaction's date, as YYYY-MM-DD
        - description: the merchant/description text exactly as shown
        - amount: the absolute dollar amount, always positive, no "$" sign or commas
        - isCredit: true if this row is a credit/payment/refund that reduces what's owed (often shown in green or
          with a leading minus sign on the card issuer's site), false if it's a normal charge

        Return ONLY a JSON array of objects with exactly these four fields (date, description, amount, isCredit) -
        no other text, no markdown code fences. If a row is a header, balance summary, or otherwise not an actual
        transaction, omit it.
        """;

    public static List<ExtractedChargeRow> ParseResponse(string responseText)
    {
        var json = StripMarkdownFences(responseText);
        var rawRows = JsonSerializer.Deserialize<List<RawExtractedRow>>(json, JsonOptions) ?? [];
        return rawRows
            .Select(r => new ExtractedChargeRow(DateOnly.Parse(r.Date), r.Description, r.Amount, r.IsCredit))
            .ToList();
    }

    // Claude sometimes wraps the JSON in a ```json fenced block, occasionally with a
    // sentence of preamble before it - find the fences wherever they are rather than
    // assuming the response starts with one.
    private static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();
        var firstFence = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (firstFence < 0)
        {
            return trimmed;
        }

        var contentStart = trimmed.IndexOf('\n', firstFence);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return contentStart > 0 && lastFence > contentStart
            ? trimmed[(contentStart + 1)..lastFence].Trim()
            : trimmed;
    }

    private record RawExtractedRow(
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("isCredit")] bool IsCredit);
}
