using System.Text;
using Expense.Domain.Services.Ingestion.Amazon;
using Google.Apis.Gmail.v1.Data;

namespace Expense.Domain.Tests.Services.Ingestion.Amazon;

public class GmailMessageParsingTests
{
    private static string EncodeBase64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [Fact]
    public void GetHeader_ReturnsTheNamedHeaderValue()
    {
        var message = new Message
        {
            Payload = new MessagePart
            {
                Headers = [new MessagePartHeader { Name = "Subject", Value = "Your Amazon.com order" }]
            }
        };

        Assert.Equal("Your Amazon.com order", GmailMessageParsing.GetHeader(message, "Subject"));
    }

    [Fact]
    public void GetHeader_WhenMissing_ReturnsAPlaceholder()
    {
        var message = new Message { Payload = new MessagePart { Headers = [] } };

        Assert.Equal("(no subject)", GmailMessageParsing.GetHeader(message, "Subject"));
    }

    [Fact]
    public void ExtractPlainTextBody_FindsATopLevelPlainTextPart()
    {
        var part = new MessagePart
        {
            MimeType = "text/plain",
            Body = new MessagePartBody { Data = EncodeBase64Url("Order confirmation body") }
        };

        Assert.Equal("Order confirmation body", GmailMessageParsing.ExtractPlainTextBody(part));
    }

    [Fact]
    public void ExtractPlainTextBody_RecursesIntoMultipartMessages()
    {
        var part = new MessagePart
        {
            MimeType = "multipart/alternative",
            Parts =
            [
                new MessagePart { MimeType = "text/html", Body = new MessagePartBody { Data = EncodeBase64Url("<p>html</p>") } },
                new MessagePart { MimeType = "text/plain", Body = new MessagePartBody { Data = EncodeBase64Url("plain text version") } }
            ]
        };

        Assert.Equal("plain text version", GmailMessageParsing.ExtractPlainTextBody(part));
    }

    [Fact]
    public void ExtractPlainTextBody_WhenNoPlainTextPartExists_ReturnsNull()
    {
        var part = new MessagePart
        {
            MimeType = "multipart/alternative",
            Parts = [new MessagePart { MimeType = "text/html", Body = new MessagePartBody { Data = EncodeBase64Url("<p>html only</p>") } }]
        };

        Assert.Null(GmailMessageParsing.ExtractPlainTextBody(part));
    }

    [Fact]
    public void DecodeBase64Url_HandlesAllThreePaddingCases()
    {
        Assert.Equal("ab", GmailMessageParsing.DecodeBase64Url(EncodeBase64Url("ab")));
        Assert.Equal("abc", GmailMessageParsing.DecodeBase64Url(EncodeBase64Url("abc")));
        Assert.Equal("abcd", GmailMessageParsing.DecodeBase64Url(EncodeBase64Url("abcd")));
    }
}
