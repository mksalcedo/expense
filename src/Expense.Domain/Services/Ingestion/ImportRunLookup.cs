using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion;

/// <summary>
/// Reads the most recent ImportRun for a source. Deliberately separate from
/// SimpleFinSyncService/AmazonGmailSyncService - the Dashboard needs to show "last
/// synced" on every page load, and neither sync service can be constructed for that
/// (AmazonGmailSyncService in particular needs a live, OAuth-authorized GmailService,
/// which we don't want to trigger just to read a timestamp).
/// </summary>
public static class ImportRunLookup
{
    public static Task<ImportRun?> GetLastRunAsync(ExpenseDbContext context, ImportSource source, CancellationToken cancellationToken = default) =>
        context.ImportRuns
            .Where(r => r.Source == source)
            .OrderByDescending(r => r.RanAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Like <see cref="GetLastRunAsync"/> but skips failed runs - used to compute an
    /// incremental sync window, where a failed run shouldn't narrow how far back the next
    /// attempt looks.
    /// </summary>
    public static Task<ImportRun?> GetLastSuccessfulRunAsync(ExpenseDbContext context, ImportSource source, CancellationToken cancellationToken = default) =>
        context.ImportRuns
            .Where(r => r.Source == source && r.Success)
            .OrderByDescending(r => r.RanAt)
            .FirstOrDefaultAsync(cancellationToken);
}
