using System.Text.Json;
using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.Amazon;
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
    IConfiguration configuration,
    SyncIssueService syncIssues) : ISyncStatusProvider
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

    public async Task<ImportRun> RunSimpleFinSyncAsync(CancellationToken cancellationToken = default)
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

        return await simpleFinSync.RunAsync(context, accessUrl, accountMap, DateTimeOffset.UtcNow.AddDays(-45), cancellationToken);
    }

    public async Task<ImportRun> RunAmazonGmailSyncAsync(CancellationToken cancellationToken = default)
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
        var result = await syncService.RunAsync(context, cancellationToken);
        return result.Run;
    }

    public async Task<List<SyncIssue>> GetActiveSyncIssuesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await syncIssues.GetActiveAsync(context, cancellationToken);
    }

    public async Task DismissSyncIssueAsync(int syncIssueId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await syncIssues.DismissAsync(context, syncIssueId, cancellationToken);
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
