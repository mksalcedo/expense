using System.Diagnostics;
using System.Text.Json;
using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Services.Ingestion.Plaid;
using Expense.Domain.Services.Ingestion.SimpleFin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Expense.Domain.Services.Dashboard;

/// <summary>
/// Thin composition root for the Dashboard's "Sync Now" buttons - loads the SimpleFin
/// access URL/account map and the Gmail OAuth credentials from local config/disk, same
/// as the two console importers, and delegates to the shared sync services. Like
/// Program.cs's own composition, this class is deliberately not unit-tested; Dashboard.razor
/// is tested against a fake ISyncStatusProvider instead.
/// </summary>
public class SyncStatusProvider(
    IDbContextFactory<ExpenseDbContext> contextFactory,
    SimpleFinSyncService simpleFinSync,
    AmazonImportService amazonImportService,
    CategorizationService categorization,
    DedupService dedup,
    IConfiguration configuration,
    SyncIssueService syncIssues,
    IForecastResultProvider forecastResultProvider,
    ForecastSnapshotService forecastSnapshotService,
    IDataChangeNotifier dataChangeNotifier) : ISyncStatusProvider
{
    public async Task<ImportRun?> GetLastSimpleFinRunAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ImportRunLookup.GetLastRunAsync(context, ImportSource.SimpleFin, cancellationToken);
    }

    public async Task<ImportRun?> GetLastAmazonGmailRunAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ImportRunLookup.GetLastRunAsync(context, ImportSource.AmazonGmail, cancellationToken);
    }

    public async Task<ImportRun?> GetLastPlaidRunAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ImportRunLookup.GetLastRunAsync(context, ImportSource.Plaid, cancellationToken);
    }

    // Wraps *CoreAsync so IDataChangeNotifier fires exactly once regardless of which
    // internal branch (success, or one of several early-return failure paths) was taken,
    // without needing to touch every one of those return points individually.
    public async Task<ImportRun> RunSimpleFinSyncAsync(CancellationToken cancellationToken = default)
    {
        var run = await RunSimpleFinSyncCoreAsync(cancellationToken);
        dataChangeNotifier.NotifyChanged();
        return run;
    }

    private async Task<ImportRun> RunSimpleFinSyncCoreAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var accessUrl = configuration["SimpleFin:AccessUrl"];
        var accountMapPath = Path.Combine(AppContext.BaseDirectory, "simplefin-account-map.json");

        if (accessUrl is null || !File.Exists(accountMapPath))
        {
            return await RecordConfigurationFailureAsync(
                context, ImportSource.SimpleFin,
                "SimpleFin is not configured (missing SimpleFin:AccessUrl secret or simplefin-account-map.json).",
                cancellationToken);
        }

        var accountMap = JsonSerializer.Deserialize<Dictionary<string, int>>(await File.ReadAllTextAsync(accountMapPath, cancellationToken))
            ?? [];

        var run = await simpleFinSync.RunAsync(context, accessUrl, accountMap, DateTimeOffset.UtcNow.AddDays(-45), cancellationToken);
        await categorization.ReapplyRulesToPendingAsync(context);
        await CaptureForecastSnapshotAsync(cancellationToken);
        return run;
    }

    public async Task<ImportRun> RunAmazonGmailSyncAsync(Action<SyncProgressLine>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var run = await RunAmazonGmailSyncCoreAsync(onProgress, cancellationToken);
        dataChangeNotifier.NotifyChanged();
        return run;
    }

    private async Task<ImportRun> RunAmazonGmailSyncCoreAsync(Action<SyncProgressLine>? onProgress, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "expense");
        var credentialsPath = Path.Combine(configDir, "gmail-credentials.json");
        var tokenStorePath = Path.Combine(configDir, "gmail-token");

        var gmail = await GmailServiceFactory.TryCreateAsync(credentialsPath, tokenStorePath, cancellationToken);
        if (gmail is null)
        {
            return await RecordConfigurationFailureAsync(
                context, ImportSource.AmazonGmail,
                $"No Gmail OAuth credentials found at {credentialsPath}.",
                cancellationToken);
        }

        var syncService = new AmazonGmailSyncService(new GoogleGmailMessageSource(gmail), amazonImportService, categorization);
        var result = await syncService.RunAsync(context, onProgress, cancellationToken);
        await CaptureForecastSnapshotAsync(cancellationToken);
        return result.Run;
    }

    // Manual, on-demand run with an explicit date range - the Import Data page's own
    // "Run Plaid Import" button. Shells out to the real plaid-cli (already linked to the
    // user's own Plaid account) exactly the way the console importer's usage instructions
    // already describe running it by hand.
    public async Task<ImportRun> RunPlaidImportAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        var run = await RunPlaidImportCoreAsync(startDate, endDate, cancellationToken);
        dataChangeNotifier.NotifyChanged();
        return run;
    }

    // Scheduled run - see PlaidSyncWindowCalculator for why the window is "OverlapDays
    // before the last successful run" with no separate bootstrap-window special case (the
    // manual date-range picker above is the deliberate safety net for anything this narrow
    // window might miss).
    public async Task<ImportRun> RunScheduledPlaidSyncAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var lastSuccessfulRun = await ImportRunLookup.GetLastSuccessfulRunAsync(context, ImportSource.Plaid, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var startDate = PlaidSyncWindowCalculator.GetWindowStartDate(lastSuccessfulRun?.RanAt, now);
        var endDate = DateOnly.FromDateTime(now.DateTime);
        var run = await RunPlaidImportCoreAsync(startDate, endDate, cancellationToken);
        dataChangeNotifier.NotifyChanged();
        return run;
    }

    private async Task<ImportRun> RunPlaidImportCoreAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        var plaidCliPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "bin", "plaid-cli");
        if (!File.Exists(plaidCliPath))
        {
            await using var failureContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await RecordConfigurationFailureAsync(failureContext, ImportSource.Plaid, $"plaid-cli not found at {plaidCliPath}.", cancellationToken);
        }

        var accountMapPath = Path.Combine(AppContext.BaseDirectory, "plaid-account-map.json");
        if (!File.Exists(accountMapPath))
        {
            await using var failureContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await RecordConfigurationFailureAsync(
                failureContext, ImportSource.Plaid,
                $"No account map found at {accountMapPath} - copy plaid-account-map.example.json and fill in real account IDs.",
                cancellationToken);
        }

        var accountMap = JsonSerializer.Deserialize<Dictionary<string, int>>(await File.ReadAllTextAsync(accountMapPath, cancellationToken)) ?? [];

        var startInfo = new ProcessStartInfo(plaidCliPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("transactions");
        startInfo.ArgumentList.Add("list");
        startInfo.ArgumentList.Add("--start-date");
        startInfo.ArgumentList.Add(startDate.ToString("yyyy-MM-dd"));
        startInfo.ArgumentList.Add("--end-date");
        startInfo.ArgumentList.Add(endDate.ToString("yyyy-MM-dd"));
        // Required once more than one item is linked (e.g. checking + Amex) - plaid-cli
        // otherwise refuses to run at all, asking which single item to query. Pulling
        // every linked item in one call is fine here since accountMap already routes each
        // transaction to the right local account regardless of which item it came from.
        startInfo.ArgumentList.Add("--all");
        startInfo.ArgumentList.Add("--json");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            await using var failureContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await RecordConfigurationFailureAsync(failureContext, ImportSource.Plaid, "Failed to start plaid-cli.", cancellationToken);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            await using var failureContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await RecordConfigurationFailureAsync(
                failureContext, ImportSource.Plaid, $"plaid-cli exited with code {process.ExitCode}: {stderr}", cancellationToken);
        }

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var service = new PlaidTransactionImportService(dedup, categorization);

            // Captured (not live-streamed) - unlike Amazon Gmail sync's per-message loop
            // over the network, a Plaid import is one fast local call, so there's no
            // real-time modal value here, only the persisted transcript reviewable
            // afterward via "View details".
            var progressLines = new List<ImportRunProgressLine>();
            void CaptureProgress(SyncProgressLine line) =>
                progressLines.Add(new ImportRunProgressLine { Sequence = progressLines.Count, Text = line.Text, IsError = line.IsError });

            var summary = await service.ImportAsync(context, stdout, accountMap, DateTimeOffset.UtcNow, CaptureProgress, cancellationToken);
            await categorization.ReapplyRulesToPendingAsync(context);

            var run = new ImportRun
            {
                Source = ImportSource.Plaid,
                RanAt = DateTimeOffset.UtcNow,
                Success = true,
                Summary = $"Transactions added: {summary.TransactionsAdded}, duplicates skipped: {summary.DuplicatesSkipped}, " +
                          $"pending transactions updated: {summary.PendingTransactionsUpdated}, balance snapshots added: {summary.BalanceSnapshotsAdded}",
                ProgressLines = progressLines,
                RawResponse = stdout
            };
            context.ImportRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);
            await CaptureForecastSnapshotAsync(cancellationToken);

            return run;
        }
        catch (Exception ex)
        {
            // A raw exception here would otherwise crash the whole Blazor circuit (see the
            // real "no transactions array" parse failure this guarded against) instead of
            // just showing an error on this one page - same friendly-failure shape as the
            // other early-return cases above.
            await using var failureContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await RecordConfigurationFailureAsync(failureContext, ImportSource.Plaid, $"Import failed: {ex.Message}", cancellationToken);
        }
    }

    public async Task<RecentRunsPage> GetRecentRunsAsync(ImportSource source, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.ImportRuns.Where(r => r.Source == source);
        var totalCount = await query.CountAsync(cancellationToken);
        var runs = await query
            .OrderByDescending(r => r.RanAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new RecentRunsPage { Runs = runs, TotalCount = totalCount };
    }

    public async Task<List<SyncProgressLine>> GetRunProgressLogAsync(int importRunId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ImportRunProgressLines
            .Where(p => p.ImportRunId == importRunId)
            .OrderBy(p => p.Sequence)
            .Select(p => new SyncProgressLine(p.Text, p.IsError))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SyncIssue>> GetActiveSyncIssuesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await syncIssues.GetActiveAsync(context, cancellationToken);
    }

    public async Task ResolveSyncIssueAsync(
        int syncIssueId, string orderId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await syncIssues.ResolveAsync(context, syncIssueId, orderId, itemTitle, price, quantity, cancellationToken);
    }

    public async Task IgnoreSyncIssueAsync(int syncIssueId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await syncIssues.IgnoreAsNotAnOrderAsync(context, syncIssueId, cancellationToken);
    }

    // Runs after every successful sync (scheduled or manual "Sync Now" click alike, since
    // both paths funnel through this class) so a snapshot is always as fresh as the data
    // that just landed, rather than only twice a day - see docs/forecast-accuracy-plan.md.
    // CaptureAsync upserts by day, so multiple captures on the same day just refine it.
    private async Task CaptureForecastSnapshotAsync(CancellationToken cancellationToken)
    {
        var forecast = await forecastResultProvider.GetForecastAsync(cancellationToken);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await forecastSnapshotService.CaptureAsync(context, forecast, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
    }

    private static async Task<ImportRun> RecordConfigurationFailureAsync(
        ExpenseDbContext context, ImportSource source, string errorMessage, CancellationToken cancellationToken)
    {
        var run = new ImportRun { Source = source, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = errorMessage };
        context.ImportRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return run;
    }
}
