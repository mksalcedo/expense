using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
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
    // Used only when there's no prior successful run to base an incremental window on (a
    // brand-new mailbox sync): generous enough to backfill roughly a year of history for
    // Historical Analysis, while dedup (order_id + item_title) makes re-running safe
    // regardless of overlap. Not yet configurable - a hardcoded value.
    private const string FallbackLookbackWindow = "newer_than:400d";

    // Every run after the first searches from just before the last successful run instead
    // of re-scanning the full fallback window - dedup makes this safe, so the overlap only
    // needs to cover Gmail's day-granularity "after:" filter and any late-arriving mail,
    // not a wide margin of safety.
    private const int OverlapDays = 4;

    /// <summary>
    /// onProgress, if given, is invoked synchronously and directly (not via IProgress&lt;T&gt;'s
    /// SynchronizationContext-based marshaling) - the caller's own await chain already keeps
    /// execution on whatever context it started on (e.g. a Blazor Server circuit), and a plain
    /// callback is simpler to test deterministically than IProgress&lt;T&gt;'s ThreadPool-hop
    /// when there's no ambient SynchronizationContext (as in a unit test).
    /// </summary>
    public async Task<AmazonGmailSyncResult> RunAsync(
        ExpenseDbContext context, Action<SyncProgressLine>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var run = new ImportRun { Source = ImportSource.AmazonGmail, RanAt = startedAt, Success = false };
        var result = new AmazonGmailSyncResult { Run = run };

        try
        {
            var lastSuccessfulRun = await ImportRunLookup.GetLastSuccessfulRunAsync(context, ImportSource.AmazonGmail, cancellationToken);
            string window;
            if (lastSuccessfulRun is null)
            {
                window = FallbackLookbackWindow;
                onProgress?.Invoke(new SyncProgressLine("No prior successful sync found - scanning the last 400 days."));
            }
            else
            {
                var windowStartDate = lastSuccessfulRun.RanAt.AddDays(-OverlapDays);
                window = $"after:{windowStartDate:yyyy/MM/dd}";
                onProgress?.Invoke(new SyncProgressLine(
                    $"Last successful sync: {lastSuccessfulRun.RanAt.ToLocalTime():MM/dd/yyyy h:mm tt} - " +
                    $"scanning since {windowStartDate:yyyy/MM/dd} ({OverlapDays}-day overlap)."));
            }

            var orderMessages = await messageSource.SearchAsync($"from:auto-confirm@amazon.com {window}", cancellationToken);
            onProgress?.Invoke(new SyncProgressLine($"Found {orderMessages.Count} order confirmation email(s) to check."));
            foreach (var message in orderMessages)
            {
                if (message.PlainTextBody is null)
                {
                    await RecordParseFailureAsync(context, result, message, "could not extract a plain-text body", onProgress, cancellationToken);
                    continue;
                }

                try
                {
                    var summary = await importService.ImportOrderAsync(context, message.PlainTextBody, message.ReceivedDate, cancellationToken);
                    result.ItemsAdded += summary.ItemsAdded;
                    result.DuplicatesSkipped += summary.DuplicatesSkipped;
                    onProgress?.Invoke(new SyncProgressLine(FormatMessageProgress(message, summary.ItemOutcomes)));
                }
                catch (FormatException ex)
                {
                    await RecordParseFailureAsync(context, result, message, ex.Message, onProgress, cancellationToken);
                }
            }

            var refundMessages = await messageSource.SearchAsync($"from:payments-messages@amazon.com {window}", cancellationToken);
            onProgress?.Invoke(new SyncProgressLine($"Found {refundMessages.Count} refund email(s) to check."));
            foreach (var message in refundMessages)
            {
                if (message.PlainTextBody is null)
                {
                    await RecordParseFailureAsync(context, result, message, "could not extract a plain-text body", onProgress, cancellationToken);
                    continue;
                }

                try
                {
                    var summary = await importService.ImportRefundAsync(context, message.PlainTextBody, message.ReceivedDate, cancellationToken);
                    result.RefundsApplied += summary.RefundsApplied;
                    result.RefundDuplicatesSkipped += summary.RefundDuplicatesSkipped;
                    onProgress?.Invoke(new SyncProgressLine(FormatMessageProgress(message, summary.ItemOutcomes)));
                }
                catch (FormatException ex)
                {
                    await RecordParseFailureAsync(context, result, message, ex.Message, onProgress, cancellationToken);
                }
            }

            // Also sweep every still-pending item against current products, not just the
            // ones this sync just touched - catches items a prior bug, or a product created
            // since, left stuck.
            var reapplied = await categorization.ReapplyRulesToPendingAsync(context);

            run.Success = true;
            run.Summary = $"Order items added: {result.ItemsAdded}, duplicates skipped: {result.DuplicatesSkipped}, refunds applied: {result.RefundsApplied}"
                + (result.ParseFailures.Count > 0 ? $"; {result.ParseFailures.Count} email(s) failed to parse" : "")
                + (reapplied.ItemsUpdated > 0 ? $"; re-categorized {reapplied.ItemsUpdated} previously pending item(s)" : "");

            var elapsed = DateTimeOffset.UtcNow - startedAt;
            onProgress?.Invoke(new SyncProgressLine($"Done in {elapsed.TotalSeconds:0.0}s - {run.Summary}"));
        }
        catch (Exception ex)
        {
            // Broad catch is deliberate here: this is the outer boundary for a user-triggered
            // background action - any failure (e.g. an expired Gmail OAuth token) should turn
            // into a recorded, visible ImportRun on the Dashboard rather than an unhandled
            // error in the Blazor circuit.
            run.ErrorMessage = ex.Message;
            onProgress?.Invoke(new SyncProgressLine($"FAILED: {ex.Message}", IsError: true));
        }

        context.ImportRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    // One block per email: subject/date header, the full raw body, and what happened -
    // so reviewing the sync means looking at the actual evidence, not a description of it.
    private static string FormatMessageProgress(GmailMessage message, IReadOnlyList<AmazonItemOutcome> outcomes)
    {
        var resultLines = outcomes.Count == 0
            ? "(no items)"
            : string.Join("\n\n", outcomes.Select(o =>
                (o.WasDuplicate
                    ? $"• Already imported (duplicate): {o.ItemTitle}"
                    : $"• Added: {o.ItemTitle} - ${o.Price:N2} x{o.Quantity}")
                + (o.NeedsReview ? " — needs review: check Amazon order page for item details" : "")));

        return $"[{message.ReceivedDate:yyyy-MM-dd}] \"{message.Subject}\"\n--- Email body ---\n{message.PlainTextBody}\n--- Result ---\n{resultLines}";
    }

    // Adds to the in-memory summary (for this run's point-in-time display/console output)
    // and, only the first time this exact message is ever seen, a durable SyncIssue row -
    // re-scanning the same still-broken message on a later run (within the overlap window)
    // must not create a duplicate row or resurrect one the user already dismissed.
    private static async Task RecordParseFailureAsync(
        ExpenseDbContext context, AmazonGmailSyncResult result, GmailMessage message, string reason, Action<SyncProgressLine>? onProgress,
        CancellationToken cancellationToken)
    {
        result.ParseFailures.Add($"[{message.Id}] \"{message.Subject}\": {reason}");
        onProgress?.Invoke(new SyncProgressLine(
            $"[{message.ReceivedDate:yyyy-MM-dd}] \"{message.Subject}\"\n--- Email body ---\n{message.PlainTextBody ?? "(no plain-text body)"}\n--- Result ---\nFAILED: {reason}",
            IsError: true));

        var exists = await context.SyncIssues.AnyAsync(i => i.Source == ImportSource.AmazonGmail && i.MessageId == message.Id, cancellationToken);
        if (!exists)
        {
            context.SyncIssues.Add(new SyncIssue
            {
                Source = ImportSource.AmazonGmail,
                MessageId = message.Id,
                Subject = message.Subject,
                Reason = reason,
                ReceivedDate = message.ReceivedDate,
                Body = message.PlainTextBody,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }
}
