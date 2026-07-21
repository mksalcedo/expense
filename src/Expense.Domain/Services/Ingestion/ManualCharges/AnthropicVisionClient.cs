using System.Text;
using System.Text.Json;

namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>
/// Thin wrapper over Anthropic's Messages API for a single image-plus-text prompt call - no
/// SDK dependency, plain JSON over REST, matching SimpleFinClient's own approach. Deliberately
/// un-unit-tested, same pragmatic exception as SimpleFinClient/GmailServiceFactory - the real
/// logic worth testing (prompt construction, response parsing) lives in
/// AmexScreenshotParsingService instead.
/// </summary>
public class AnthropicVisionClient(HttpClient httpClient, string apiKey) : IAnthropicVisionClient
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";
    private const string Model = "claude-sonnet-5";

    public async Task<string> SendImagePromptAsync(
        byte[] imageBytes, string mediaType, string prompt, CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            model = Model,
            max_tokens = 4096,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = mediaType, data = Convert.ToBase64String(imageBytes) } },
                        new { type = "text", text = prompt }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Anthropic API request failed ({(int)response.StatusCode}): {responseJson}");
        }

        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Anthropic API response had no text content.");
    }
}
