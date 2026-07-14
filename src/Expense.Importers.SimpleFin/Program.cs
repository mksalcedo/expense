using System.Text.Json;
using Expense.Domain.Data;
using Expense.Domain.Seed;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.SimpleFin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var connectionString = config.GetConnectionString("ExpenseDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ExpenseDb not set. Run: dotnet user-secrets set \"ConnectionStrings:ExpenseDb\" \"...\"");

var options = new DbContextOptionsBuilder<ExpenseDbContext>()
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention()
    .Options;

await using var context = new ExpenseDbContext(options);
await context.Database.MigrateAsync();
await new DbSeeder().SeedAsync(context);

Console.WriteLine($"Seed complete. Categories: {await context.Categories.CountAsync()}, Accounts: {await context.Accounts.CountAsync()}");

var accessUrl = config["SimpleFin:AccessUrl"]
    ?? throw new InvalidOperationException("SimpleFin:AccessUrl not set. Run: dotnet user-secrets set \"SimpleFin:AccessUrl\" \"...\"");

var accountMapPath = Path.Combine(AppContext.BaseDirectory, "simplefin-account-map.json");
if (!File.Exists(accountMapPath))
{
    Console.WriteLine($"No account map found at {accountMapPath} - skipping SimpleFin import. Copy simplefin-account-map.example.json to simplefin-account-map.json and fill in real account IDs.");
    return;
}

var accountMap = JsonSerializer.Deserialize<Dictionary<string, int>>(await File.ReadAllTextAsync(accountMapPath))
    ?? throw new InvalidOperationException($"Could not parse {accountMapPath}");

var client = new SimpleFinClient(new HttpClient(), accessUrl);
var importService = new SimpleFinImportService(client, new DedupService(), new CategorizationService());
var summary = await importService.ImportAsync(context, accountMap, DateTimeOffset.UtcNow.AddDays(-45));

Console.WriteLine($"SimpleFin import complete. Transactions added: {summary.TransactionsAdded}, duplicates skipped: {summary.DuplicatesSkipped}, balance snapshots added: {summary.BalanceSnapshotsAdded}");
if (summary.UnmappedAccounts.Count > 0)
{
    Console.WriteLine($"Unmapped SimpleFin account IDs (not in account map): {string.Join(", ", summary.UnmappedAccounts)}");
}
