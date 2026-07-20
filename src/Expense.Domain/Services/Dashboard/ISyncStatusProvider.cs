using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion.Amazon;

namespace Expense.Domain.Services.Dashboard;

public interface ISyncStatusProvider
{
    Task<ImportRun?> GetLastSimpleFinRunAsync(CancellationToken cancellationToken = default);
    Task<ImportRun?> GetLastAmazonGmailRunAsync(CancellationToken cancellationToken = default);
    Task<ImportRun> RunSimpleFinSyncAsync(CancellationToken cancellationToken = default);
    Task<ImportRun> RunAmazonGmailSyncAsync(Action<SyncProgressLine>? onProgress = null, CancellationToken cancellationToken = default);

    Task<List<SyncIssue>> GetActiveSyncIssuesAsync(CancellationToken cancellationToken = default);
    Task ResolveSyncIssueAsync(int syncIssueId, string orderId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default);
    Task IgnoreSyncIssueAsync(int syncIssueId, CancellationToken cancellationToken = default);
}
