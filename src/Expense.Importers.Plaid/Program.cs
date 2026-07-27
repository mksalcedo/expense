using System.Text.Json;
using Expense.Domain.Data;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.Plaid;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Reads the output of `plaid-cli transactions list --json` (piped via stdin, or a file
// path passed as the first argument) and imports it as a backup data source - see
// docs/plaid-import-utility-plan.md. This is a manually-triggered, on-demand tool, not a
// scheduled sync: run it whenever you suspect SimpleFin has gone stale for an account.
//
// Usage:
//   ~/bin/plaid-cli transactions list --start-date 2026-07-15 --end-date 2026-07-25 --json \
//     | dotnet run --project src/Expense.Importers.Plaid
// or:
//   dotnet run --project src/Expense.Importers.Plaid -- plaid-output.json

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var connectionString = config.GetConnectionString("ExpenseDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ExpenseDb not set. Run: dotnet user-secrets set \"ConnectionStrings:ExpenseDb\" \"...\"");

var accountMapPath = Path.Combine(AppContext.BaseDirectory, "plaid-account-map.json");
if (!File.Exists(accountMapPath))
{
    Console.WriteLine($"No account map found at {accountMapPath} - copy plaid-account-map.example.json to plaid-account-map.json and fill in real account IDs.");
    return;
}

var accountMap = JsonSerializer.Deserialize<Dictionary<string, int>>(await File.ReadAllTextAsync(accountMapPath))
    ?? throw new InvalidOperationException($"Could not parse {accountMapPath}");

var rawCliOutput = args.Length > 0
    ? await File.ReadAllTextAsync(args[0])
    : await Console.In.ReadToEndAsync();

if (string.IsNullOrWhiteSpace(rawCliOutput))
{
    Console.WriteLine("No input received - pipe `plaid-cli transactions list --json` into this tool, or pass a file path as the first argument.");
    return;
}

var options = new DbContextOptionsBuilder<ExpenseDbContext>()
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention()
    .Options;

await using var context = new ExpenseDbContext(options);

var service = new PlaidTransactionImportService(new DedupService(), new CategorizationService());
var summary = await service.ImportAsync(context, rawCliOutput, accountMap, DateTimeOffset.UtcNow);

Console.WriteLine($"Transactions added: {summary.TransactionsAdded}");
Console.WriteLine($"Duplicates skipped: {summary.DuplicatesSkipped}");
Console.WriteLine($"Balance snapshots added: {summary.BalanceSnapshotsAdded}");
if (summary.UnmappedAccounts.Count > 0)
{
    Console.WriteLine($"Unmapped accounts (add these to plaid-account-map.json): {string.Join(", ", summary.UnmappedAccounts)}");
}

if (summary.NewTransactions.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("New transactions:");
    foreach (var t in summary.NewTransactions.OrderBy(t => t.TransactionDate))
    {
        Console.WriteLine($"  {t.TransactionDate}  {t.Amount,10:N2}  {t.Description}  (posted: {t.PostedDate?.ToString() ?? "pending"})");
    }
}
