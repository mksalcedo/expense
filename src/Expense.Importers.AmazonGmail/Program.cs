using Expense.Domain.Data;
using Expense.Domain.Services.Ingestion.Amazon;
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
var tokenStorePath = Path.Combine(configDir, "gmail-token");

var gmail = await GmailServiceFactory.TryCreateAsync(credentialsPath, tokenStorePath);
if (gmail is null)
{
    Console.WriteLine($"No Gmail OAuth credentials found at {credentialsPath}.");
    Console.WriteLine("Create a Desktop-app OAuth client in Google Cloud Console and save its downloaded JSON there.");
    return;
}

var messageSource = new GoogleGmailMessageSource(gmail);
var importService = new AmazonImportService(new AmazonOrderEmailParser(), new AmazonRefundEmailParser());
var syncService = new AmazonGmailSyncService(messageSource, importService);

Console.WriteLine("Searching Gmail for Amazon order confirmation and refund emails...");
var result = await syncService.RunAsync(context);

if (!result.Run.Success)
{
    Console.WriteLine($"Amazon import FAILED: {result.Run.ErrorMessage}");
    return;
}

Console.WriteLine();
Console.WriteLine("=== Amazon import summary ===");
Console.WriteLine($"Order items added: {result.ItemsAdded}");
Console.WriteLine($"Order items skipped as duplicates: {result.DuplicatesSkipped}");
Console.WriteLine($"Refunds applied: {result.RefundsApplied}");

if (result.UnmatchedRefunds.Count > 0)
{
    Console.WriteLine($"Refunds with no matching purchase on file ({result.UnmatchedRefunds.Count}):");
    foreach (var u in result.UnmatchedRefunds)
    {
        Console.WriteLine($"  - {u}");
    }
}

if (result.ParseFailures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"!!! {result.ParseFailures.Count} email(s) FAILED TO PARSE - review these manually !!!");
    foreach (var failure in result.ParseFailures)
    {
        Console.WriteLine($"  - {failure}");
    }
}
