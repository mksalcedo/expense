using Expense.Domain.Entities;

namespace Expense.Domain.Services.Dashboard;

public interface ISyncStatusProvider
{
    Task<ImportRun?> GetLastSimpleFinRunAsync(CancellationToken cancellationToken = default);
    Task<ImportRun?> GetLastAmazonGmailRunAsync(CancellationToken cancellationToken = default);
    Task<ImportRun> RunSimpleFinSyncAsync(CancellationToken cancellationToken = default);
    Task<ImportRun> RunAmazonGmailSyncAsync(CancellationToken cancellationToken = default);
}
