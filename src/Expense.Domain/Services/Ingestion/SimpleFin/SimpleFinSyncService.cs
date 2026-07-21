using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.SimpleFin;

/// <summary>
/// Wraps a SimpleFin import run so both the console importer and the web Dashboard's
/// "Sync Now" button share the exact same logic, and every attempt - success or
/// failure - gets recorded as an ImportRun for the Dashboard to display.
/// </summary>
public class SimpleFinSyncService(
    HttpClient httpClient, DedupService dedup, CategorizationService categorization, ManualChargeMatchingService manualChargeMatching)
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

            // Also sweep every still-pending row against current rules, not just the ones
            // this import just touched - catches rows a prior bug, or a rule created since,
            // left stuck (see the Truist whitespace-matching bug for a real example).
            var reapplied = await categorization.ReapplyRulesToPendingAsync(context);

            // A newly-posted real transaction may supersede a manually-entered pending charge
            // (see docs/amex-pending-charges-plan.md) - never silently, always reported below.
            var placeholdersRemoved = await manualChargeMatching.ReconcilePlaceholdersAsync(context, summary.NewTransactions);

            run.Success = true;
            run.Summary = $"Transactions added: {summary.TransactionsAdded}, duplicates skipped: {summary.DuplicatesSkipped}, balance snapshots added: {summary.BalanceSnapshotsAdded}"
                + (summary.UnmappedAccounts.Count > 0 ? $"; unmapped accounts: {string.Join(", ", summary.UnmappedAccounts)}" : "")
                + (reapplied.TransactionsUpdated > 0 ? $"; re-categorized {reapplied.TransactionsUpdated} previously pending transaction(s)" : "")
                + (placeholdersRemoved > 0 ? $"; removed {placeholdersRemoved} manually-entered charge(s) now confirmed posted" : "");
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
