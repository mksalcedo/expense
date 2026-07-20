using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion;

/// <summary>Read/dismiss for SyncIssue - the Dashboard's durable "sync issues need review" list.</summary>
public class SyncIssueService
{
    public Task<List<SyncIssue>> GetActiveAsync(ExpenseDbContext context, CancellationToken cancellationToken = default) =>
        context.SyncIssues
            .Where(i => !i.Dismissed)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task DismissAsync(ExpenseDbContext context, int syncIssueId, CancellationToken cancellationToken = default)
    {
        var issue = await context.SyncIssues.SingleAsync(i => i.Id == syncIssueId, cancellationToken);
        issue.Dismissed = true;
        await context.SaveChangesAsync(cancellationToken);
    }
}
