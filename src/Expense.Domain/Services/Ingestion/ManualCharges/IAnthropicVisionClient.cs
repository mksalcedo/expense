namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>Lets AmexScreenshotParsingService be tested against a fake response, same reason AmazonGmailSyncService depends on IGmailMessageSource rather than a concrete client.</summary>
public interface IAnthropicVisionClient
{
    Task<string> SendImagePromptAsync(byte[] imageBytes, string mediaType, string prompt, CancellationToken cancellationToken = default);
}
