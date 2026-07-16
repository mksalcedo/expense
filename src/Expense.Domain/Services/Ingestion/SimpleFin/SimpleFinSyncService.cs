using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.SimpleFin;

/// <summary>
/// Wraps a SimpleFin import run so both the console importer and the web Dashboard's
/// "Sync Now" button share the exact same logic, and every attempt - success or
/// failure - gets recorded as an ImportRun for the Dashboard to display.
/// </summary>
public class SimpleFinSyncService(HttpClient httpClient, DedupService dedup, CategorizationService categorization)
{
    public async Task<ImportRun> RunAsync(
        ExpenseDbContext context,
        string accessUrl,
        IReadOnlyDictionary<string, int> accountMap,
        DateTimeOffset startDate,
        CancellationToken cancellationToken = default)
    {
        var run = new ImportRun { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = false };

        try
        {
            var client = new SimpleFinClient(httpClient, accessUrl);
            var importService = new SimpleFinImportService(client, dedup, categorization);
            var summary = await importService.ImportAsync(context, accountMap, startDate, cancellationToken);

            run.Success = true;
            run.Summary = $"Transactions added: {summary.TransactionsAdded}, duplicates skipped: {summary.DuplicatesSkipped}, balance snapshots added: {summary.BalanceSnapshotsAdded}"
                + (summary.UnmappedAccounts.Count > 0 ? $"; unmapped accounts: {string.Join(", ", summary.UnmappedAccounts)}" : "");
        }
        catch (Exception ex)
        {
            // Broad catch is deliberate here: this is the outer boundary for a user-triggered
            // background action - any failure should turn into a recorded, visible ImportRun
            // on the Dashboard rather than an unhandled error in the Blazor circuit.
            run.ErrorMessage = ex.Message;
        }

        context.ImportRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return run;
    }
}
