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
}
