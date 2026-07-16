using Google.Apis.Gmail.v1;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Real Gmail-backed IGmailMessageSource. Thin composition over the Google API client -
/// same pragmatic exception to TDD as Program.cs's own composition root, since GmailService
/// isn't meaningfully fakeable without a disproportionate amount of scaffolding for a
/// personal app. AmazonGmailSyncService's actual business logic is tested against this
/// interface instead, using a fake implementation.
/// </summary>
public class GoogleGmailMessageSource(GmailService gmail) : IGmailMessageSource
{
    public async Task<IReadOnlyList<GmailMessage>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var refs = new List<Google.Apis.Gmail.v1.Data.Message>();
        string? pageToken = null;
        do
        {
            var request = gmail.Users.Messages.List("me");
            request.Q = query;
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Messages is not null)
            {
                refs.AddRange(response.Messages);
            }
            pageToken = response.NextPageToken;
        } while (pageToken is not null);

        var messages = new List<GmailMessage>();
        foreach (var messageRef in refs)
        {
            var message = await gmail.Users.Messages.Get("me", messageRef.Id).ExecuteAsync(cancellationToken);
            var subject = GmailMessageParsing.GetHeader(message, "Subject");
            var body = GmailMessageParsing.ExtractPlainTextBody(message.Payload);
            var receivedDate = message.InternalDate is { } unixMs
                ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime)
                : DateOnly.FromDateTime(DateTime.UtcNow);

            messages.Add(new GmailMessage(messageRef.Id, subject, body, receivedDate));
        }

        return messages;
    }
}
