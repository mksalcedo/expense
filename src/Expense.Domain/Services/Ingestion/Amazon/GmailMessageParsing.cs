using System.Text;
using Google.Apis.Gmail.v1.Data;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>Pure helpers for pulling a subject/plain-text body out of a raw Gmail API message, extracted from the console importer so they're independently testable.</summary>
public static class GmailMessageParsing
{
    public static string GetHeader(Message message, string name) =>
        message.Payload.Headers.FirstOrDefault(h => h.Name == name)?.Value ?? "(no subject)";

    public static string? ExtractPlainTextBody(MessagePart part)
    {
        if (part.MimeType == "text/plain" && !string.IsNullOrEmpty(part.Body?.Data))
        {
            return DecodeBase64Url(part.Body.Data);
        }

        if (part.Parts is not null)
        {
            foreach (var sub in part.Parts)
            {
                var result = ExtractPlainTextBody(sub);
                if (result is not null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    public static string DecodeBase64Url(string data)
    {
        var base64 = data.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64
        };
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
