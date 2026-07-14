using System.Text;
using Expense.Domain.Data;
using Expense.Domain.Services.Ingestion.Amazon;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
var connectionString = config.GetConnectionString("ExpenseDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ExpenseDb not set. Run: dotnet user-secrets set \"ConnectionStrings:ExpenseDb\" \"...\" --project src/Expense.Importers.AmazonGmail");

var options = new DbContextOptionsBuilder<ExpenseDbContext>()
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention()
    .Options;

await using var context = new ExpenseDbContext(options);
await context.Database.MigrateAsync();

var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "expense");
var credentialsPath = Path.Combine(configDir, "gmail-credentials.json");
if (!File.Exists(credentialsPath))
{
    Console.WriteLine($"No Gmail OAuth credentials found at {credentialsPath}.");
    Console.WriteLine("Create a Desktop-app OAuth client in Google Cloud Console and save its downloaded JSON there.");
    return;
}

var tokenStorePath = Path.Combine(configDir, "gmail-token");

UserCredential credential;
await using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
{
    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
        (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets,
        [GmailService.Scope.GmailReadonly],
        "user",
        CancellationToken.None,
        new FileDataStore(tokenStorePath, true));
}

var gmail = new GmailService(new BaseClientService.Initializer
{
    HttpClientInitializer = credential,
    ApplicationName = "Expense Amazon Importer"
});

var importService = new AmazonImportService(new AmazonOrderEmailParser(), new AmazonRefundEmailParser());
var failures = new List<(string Id, string Subject, string Error)>();

// 400-day lookback window: generous enough to backfill roughly a year of history for
// Historical Analysis on the first run, while dedup (order_id + item_title) makes
// re-running safe regardless of overlap. Not yet configurable - a hardcoded value.
const string LookbackWindow = "newer_than:400d";

Console.WriteLine("Searching for Amazon order confirmation emails...");
var orderMessages = await ListMessagesAsync(gmail, $"from:auto-confirm@amazon.com {LookbackWindow}");
Console.WriteLine($"Found {orderMessages.Count} order confirmation emails.");

var totalItemsAdded = 0;
var totalDuplicates = 0;
foreach (var messageRef in orderMessages)
{
    var message = await gmail.Users.Messages.Get("me", messageRef.Id).ExecuteAsync();
    var subject = GetHeader(message, "Subject");
    var body = ExtractPlainTextBody(message.Payload);

    if (body is null)
    {
        failures.Add((messageRef.Id, subject, "Could not extract a plain-text body from this email."));
        continue;
    }

    var orderDate = message.InternalDate is { } unixMs
        ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime)
        : DateOnly.FromDateTime(DateTime.UtcNow);

    try
    {
        var summary = await importService.ImportOrderAsync(context, body, orderDate);
        totalItemsAdded += summary.ItemsAdded;
        totalDuplicates += summary.DuplicatesSkipped;
    }
    catch (FormatException ex)
    {
        failures.Add((messageRef.Id, subject, ex.Message));
    }
}

Console.WriteLine("Searching for Amazon refund emails...");
var refundMessages = await ListMessagesAsync(gmail, $"from:payments-messages@amazon.com {LookbackWindow}");
Console.WriteLine($"Found {refundMessages.Count} refund emails.");

var totalRefundsApplied = 0;
var unmatchedRefunds = new List<string>();
foreach (var messageRef in refundMessages)
{
    var message = await gmail.Users.Messages.Get("me", messageRef.Id).ExecuteAsync();
    var subject = GetHeader(message, "Subject");
    var body = ExtractPlainTextBody(message.Payload);

    if (body is null)
    {
        failures.Add((messageRef.Id, subject, "Could not extract a plain-text body from this email."));
        continue;
    }

    try
    {
        var summary = await importService.ImportRefundAsync(context, body);
        totalRefundsApplied += summary.RefundsApplied;
        unmatchedRefunds.AddRange(summary.UnmatchedRefunds);
    }
    catch (FormatException ex)
    {
        failures.Add((messageRef.Id, subject, ex.Message));
    }
}

Console.WriteLine();
Console.WriteLine("=== Amazon import summary ===");
Console.WriteLine($"Order items added: {totalItemsAdded}");
Console.WriteLine($"Order items skipped as duplicates: {totalDuplicates}");
Console.WriteLine($"Refunds applied: {totalRefundsApplied}");

if (unmatchedRefunds.Count > 0)
{
    Console.WriteLine($"Refunds with no matching purchase on file ({unmatchedRefunds.Count}):");
    foreach (var u in unmatchedRefunds)
    {
        Console.WriteLine($"  - {u}");
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"!!! {failures.Count} email(s) FAILED TO PARSE - review these manually !!!");
    foreach (var (id, subject, error) in failures)
    {
        Console.WriteLine($"  - [{id}] \"{subject}\": {error}");
    }
}

static string GetHeader(Message message, string name) =>
    message.Payload.Headers.FirstOrDefault(h => h.Name == name)?.Value ?? "(no subject)";

static async Task<List<Message>> ListMessagesAsync(GmailService gmail, string query)
{
    var results = new List<Message>();
    string? pageToken = null;
    do
    {
        var request = gmail.Users.Messages.List("me");
        request.Q = query;
        request.PageToken = pageToken;
        var response = await request.ExecuteAsync();
        if (response.Messages is not null)
        {
            results.AddRange(response.Messages);
        }
        pageToken = response.NextPageToken;
    } while (pageToken is not null);

    return results;
}

static string? ExtractPlainTextBody(MessagePart part)
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

static string DecodeBase64Url(string data)
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
