using System.Text.Json;
using Expense.Domain.Services.Ingestion.ManualCharges;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Extracts item title(s) from a screenshot of an Amazon order-details page - the fallback for
/// a NeedsReview item whose confirmation email never had item-level detail in the first place
/// (see docs/amazon-needs-review-plan.md's deferred "fourth idea"). Reuses the same
/// IAnthropicVisionClient as AmexScreenshotParsingService; the actual API call is delegated so
/// this prompt/parsing logic can be unit tested without a live call.
/// </summary>
public class AmazonOrderScreenshotParsingService(IAnthropicVisionClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<string>> ParseScreenshotAsync(byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
    {
        var responseText = await client.SendImagePromptAsync(imageBytes, mediaType, BuildPrompt(), cancellationToken);
        return ParseResponse(responseText);
    }

    public static string BuildPrompt() => """
        You are extracting the product name(s) from a screenshot of an Amazon order-details page.

        List every distinct item/product actually part of this order, using its full product title exactly as
        shown (not a shortened or generic description). Ignore shipping/tax/subtotal/total lines, "Buy it again"
        or "Related products" suggestions, and anything else that isn't actually part of this order.

        Return ONLY a JSON array of strings (the item titles), no other text, no markdown code fences. If you
        can't confidently identify any real item title, return an empty array.
        """;

    public static List<string> ParseResponse(string responseText)
    {
        var json = StripMarkdownFences(responseText);
        return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
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
}
