using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Wraps an Amazon Gmail sync run so both the console importer and the web Dashboard's
/// "Sync Now" button share the exact same logic, and every attempt - success or
/// failure - gets recorded as an ImportRun for the Dashboard to display. Depends on
/// IGmailMessageSource rather than GmailService directly so this orchestration (looping
/// over messages, importing, collecting parse failures, summarizing) is testable without
/// a real Gmail connection; the OAuth/network-fetching side stays a thin, un-mocked
/// implementation of that interface, same pragmatic exception as Program.cs composition.
/// </summary>
public class AmazonGmailSyncService(IGmailMessageSource messageSource, AmazonImportService importService, CategorizationService categorization)
{
    // 400-day lookback window: generous enough to backfill roughly a year of history for
    // Historical Analysis on the first run, while dedup (order_id + item_title) makes
    // re-running safe regardless of overlap. Not yet configurable - a hardcoded value.
    private const string LookbackWindow = "newer_than:400d";

    public async Task<AmazonGmailSyncResult> RunAsync(ExpenseDbContext context, CancellationToken cancellationToken = default)
    {
        var run = new ImportRun { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = false };
        var result = new AmazonGmailSyncResult { Run = run };

        try
        {
            var orderMessages = await messageSource.SearchAsync($"from:auto-confirm@amazon.com {LookbackWindow}", cancellationToken);
            foreach (var message in orderMessages)
            {
                if (message.PlainTextBody is null)
                {
                    result.ParseFailures.Add($"[{message.Id}] \"{message.Subject}\": could not extract a plain-text body");
                    continue;
                }

                try
                {
                    var summary = await importService.ImportOrderAsync(context, message.PlainTextBody, message.ReceivedDate, cancellationToken);
                    result.ItemsAdded += summary.ItemsAdded;
                    result.DuplicatesSkipped += summary.DuplicatesSkipped;
                }
                catch (FormatException ex)
                {
                    result.ParseFailures.Add($"[{message.Id}] \"{message.Subject}\": {ex.Message}");
                }
            }

            var refundMessages = await messageSource.SearchAsync($"from:payments-messages@amazon.com {LookbackWindow}", cancellationToken);
            foreach (var message in refundMessages)
            {
                if (message.PlainTextBody is null)
                {
                    result.ParseFailures.Add($"[{message.Id}] \"{message.Subject}\": could not extract a plain-text body");
                    continue;
                }

                try
                {
                    var summary = await importService.ImportRefundAsync(context, message.PlainTextBody, cancellationToken);
                    result.RefundsApplied += summary.RefundsApplied;
                    result.UnmatchedRefunds.AddRange(summary.UnmatchedRefunds);
                }
                catch (FormatException ex)
                {
                    result.ParseFailures.Add($"[{message.Id}] \"{message.Subject}\": {ex.Message}");
                }
            }

            // Also sweep every still-pending item against current products, not just the
            // ones this sync just touched - catches items a prior bug, or a product created
            // since, left stuck.
            var reapplied = await categorization.ReapplyRulesToPendingAsync(context);

            run.Success = true;
            run.Summary = $"Order items added: {result.ItemsAdded}, duplicates skipped: {result.DuplicatesSkipped}, refunds applied: {result.RefundsApplied}"
                + (result.UnmatchedRefunds.Count > 0 ? $"; unmatched refunds: {result.UnmatchedRefunds.Count}" : "")
                + (result.ParseFailures.Count > 0 ? $"; {result.ParseFailures.Count} email(s) failed to parse" : "")
                + (reapplied.ItemsUpdated > 0 ? $"; re-categorized {reapplied.ItemsUpdated} previously pending item(s)" : "");
        }
        catch (Exception ex)
        {
            // Broad catch is deliberate here: this is the outer boundary for a user-triggered
            // background action - any failure (e.g. an expired Gmail OAuth token) should turn
            // into a recorded, visible ImportRun on the Dashboard rather than an unhandled
            // error in the Blazor circuit.
            run.ErrorMessage = ex.Message;
        }

        context.ImportRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }
}
