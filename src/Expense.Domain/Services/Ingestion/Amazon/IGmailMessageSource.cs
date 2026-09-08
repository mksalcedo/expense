namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// One matched Gmail message's data needed for Amazon order/refund parsing, decoupled from
/// the Google API's own types so AmazonGmailSyncService's orchestration logic is testable
/// without a real Gmail connection. HtmlBody is the text/html MIME part - carries the
/// per-order department hint that text/plain omits (see AmazonOrderCategoryHintParser).
/// </summary>
public record GmailMessage(string Id, string Subject, string? PlainTextBody, DateOnly ReceivedDate, string? HtmlBody = null);

public interface IGmailMessageSource
{
    Task<IReadOnlyList<GmailMessage>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
