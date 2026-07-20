using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion.Amazon;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion;

/// <summary>
/// Read/resolve for SyncIssue - the Dashboard's "sync issues need review" list. Resolving
/// one is the actual fix (creating the missing AmazonOrderItem by hand from the details
/// already captured on the issue), not just hiding it - see SyncIssue's own doc comment.
/// </summary>
public class SyncIssueService(AmazonImportService amazonImportService)
{
    public Task<List<SyncIssue>> GetActiveAsync(ExpenseDbContext context, CancellationToken cancellationToken = default) =>
        context.SyncIssues
            .Where(i => i.Resolution == SyncIssueResolution.None)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task ResolveAsync(
        ExpenseDbContext context, int syncIssueId, string orderId, string itemTitle, decimal price, int quantity,
        CancellationToken cancellationToken = default)
    {
        var issue = await context.SyncIssues.SingleAsync(i => i.Id == syncIssueId, cancellationToken);
        var item = await amazonImportService.AddManualItemAsync(context, orderId, issue.ReceivedDate, itemTitle, price, quantity, cancellationToken);

        issue.Resolution = SyncIssueResolution.Resolved;
        issue.ResolvedAmazonOrderItemId = item.Id;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task IgnoreAsNotAnOrderAsync(ExpenseDbContext context, int syncIssueId, CancellationToken cancellationToken = default)
    {
        var issue = await context.SyncIssues.SingleAsync(i => i.Id == syncIssueId, cancellationToken);
        issue.Resolution = SyncIssueResolution.NotAnOrder;
        await context.SaveChangesAsync(cancellationToken);
    }
}
