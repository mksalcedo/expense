using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>
/// Fuzzy account+amount+date-window matching used in both directions for manually-entered
/// (screenshot-derived) pending charges - see docs/amex-pending-charges-plan.md. This is
/// deliberately not exact-fingerprint matching like DedupService: a manually-entered charge
/// has no real bank-issued ID and its date is only ever an approximation (what the user saw
/// on a pending list), so the match has to tolerate a few days of drift. Exact amount match is
/// the strong signal - two different real charges landing on the exact same cent within the
/// window, on the same account, is rare enough to trust.
/// </summary>
public class ManualChargeMatchingService
{
    public const string ManualScreenshotImportSource = "ManualScreenshot";
    public const int MatchWindowDays = 5;

    /// <summary>
    /// Used before adding a freshly-extracted screenshot row: is this already tracked at all,
    /// regardless of source (a normal synced transaction, or an earlier manual entry)?
    /// </summary>
    public async Task<BankTransaction?> FindExistingMatchAsync(
        ExpenseDbContext context, int accountId, DateOnly date, decimal amount, CancellationToken cancellationToken = default)
    {
        var windowStart = date.AddDays(-MatchWindowDays);
        var windowEnd = date.AddDays(MatchWindowDays);

        return await context.BankTransactions
            .Where(t => t.AccountId == accountId && t.Amount == amount)
            .Where(t => (t.PostedDate ?? t.TransactionDate) >= windowStart && (t.PostedDate ?? t.TransactionDate) <= windowEnd)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Used after a normal sync brings in a real, newly-posted transaction: does it correspond
    /// to a still-open manually-entered placeholder that should now be removed in its favor?
    /// </summary>
    public async Task<BankTransaction?> FindOpenPlaceholderMatchAsync(
        ExpenseDbContext context, int accountId, DateOnly postedDate, decimal amount, CancellationToken cancellationToken = default)
    {
        var windowStart = postedDate.AddDays(-MatchWindowDays);
        var windowEnd = postedDate.AddDays(MatchWindowDays);

        return await context.BankTransactions
            .Where(t => t.AccountId == accountId && t.Amount == amount
                        && t.ImportSource == ManualScreenshotImportSource && t.PostedDate == null)
            .Where(t => t.TransactionDate >= windowStart && t.TransactionDate <= windowEnd)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Called after a normal sync - for every newly-imported real transaction, removes the
    /// open manually-entered placeholder it supersedes, if any. Never silent: returns how many
    /// were removed so the caller can report it in the sync's own summary text.
    /// </summary>
    public async Task<int> ReconcilePlaceholdersAsync(
        ExpenseDbContext context, IEnumerable<BankTransaction> newlyImportedTransactions, CancellationToken cancellationToken = default)
    {
        var removed = 0;
        foreach (var transaction in newlyImportedTransactions)
        {
            if (transaction.PostedDate is not { } postedDate) continue;

            var placeholder = await FindOpenPlaceholderMatchAsync(context, transaction.AccountId, postedDate, transaction.Amount, cancellationToken);
            if (placeholder is not null)
            {
                context.BankTransactions.Remove(placeholder);
                removed++;
            }
        }

        if (removed > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return removed;
    }

    /// <summary>
    /// Lets the user remove a still-open placeholder that will apparently never post (e.g. a
    /// voided authorization) - see the "known residual risk" in docs/amex-pending-charges-plan.md.
    /// Only ever deletes an open ManualScreenshot placeholder, never any other transaction, even
    /// if a stale/incorrect id is passed in.
    /// </summary>
    public async Task DeletePlaceholderAsync(ExpenseDbContext context, int transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await context.BankTransactions.FindAsync([transactionId], cancellationToken);
        if (transaction is null || transaction.ImportSource != ManualScreenshotImportSource || transaction.PostedDate is not null)
        {
            return;
        }

        context.BankTransactions.Remove(transaction);
        await context.SaveChangesAsync(cancellationToken);
    }
}
