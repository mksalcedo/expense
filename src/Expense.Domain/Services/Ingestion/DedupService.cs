using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion;

/// <summary>
/// Dedupe via bank transaction ID when available, else a fingerprint of
/// account + posted date + amount + normalized description, with an occurrence
/// index as a tiebreaker for genuinely identical duplicate charges (e.g. two
/// separate $12 QuikTrip purchases on the same day). Same principle applies to
/// Amazon order ingestion (dedupe by Order ID), handled separately there since
/// Amazon already supplies a real unique ID.
/// </summary>
public class DedupService
{
    public static string NormalizeDescription(string raw) =>
        string.Join(' ', raw.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static string GenerateFingerprint(int accountId, DateOnly postedDate, decimal amount, string description, int occurrenceIndex = 0)
    {
        var normalized = NormalizeDescription(description);
        return $"{accountId}|{postedDate:yyyy-MM-dd}|{amount:F2}|{normalized}|{occurrenceIndex}";
    }

    public async Task<bool> ExistsAsync(ExpenseDbContext context, int accountId, string? externalId, string? fingerprint)
    {
        if (!string.IsNullOrEmpty(externalId))
        {
            return await context.BankTransactions
                .AnyAsync(t => t.AccountId == accountId && t.ExternalId == externalId);
        }

        if (!string.IsNullOrEmpty(fingerprint))
        {
            return await context.BankTransactions.AnyAsync(t => t.DedupFingerprint == fingerprint);
        }

        return false;
    }

    /// <summary>
    /// Cross-source dedup, deliberately ignoring ExternalId/description: two aggregators
    /// covering the same real account will eventually both report the same real
    /// transaction under different IDs and different description text (raw bank text vs.
    /// a cleaned-up merchant name) - account+posted date+amount is the only signal both
    /// sources can be expected to agree on. Accepted, narrow risk: two genuinely different
    /// real transactions same account/day/amount would collide here - rare in practice.
    /// </summary>
    public async Task<bool> ExistsForAccountDateAmountAsync(ExpenseDbContext context, int accountId, DateOnly postedDate, decimal amount) =>
        await context.BankTransactions.AnyAsync(t => t.AccountId == accountId && t.PostedDate == postedDate && t.Amount == amount);

    /// <summary>
    /// Finds the still-open pending row a newly-posted transaction represents, so the
    /// caller can merge into it instead of inserting a duplicate. Used as a fallback when
    /// no direct id link is available - Plaid's own pending_transaction_id, when supplied,
    /// is a more precise primary match and should be tried first; this exists because that
    /// field is only populated "when available" (confirmed missing for two real
    /// transactions on 2026-07-29), and because a cross-source pair (e.g. a Plaid-pending
    /// row later reported as posted by SimpleFin) has no shared id concept at all -
    /// ExistsForAccountDateAmountAsync can never catch that case since it compares by
    /// PostedDate, which a pending row never has. Matches by account + amount, with
    /// TransactionDate allowed to drift up to windowDays either direction - confirmed for
    /// real that a pending charge's own date can differ from the eventual posted
    /// transaction's date by several days (a real Chick-fil-A charge drifted 4 days).
    /// Scoped to ImportSource == "Plaid" only - manually-entered placeholder charges
    /// (ManualChargeMatchingService.ManualScreenshotImportSource) also have PostedDate ==
    /// null while awaiting the real transaction, but already have their own dedicated
    /// removal-on-match mechanism; this must not intercept them first (confirmed for real
    /// - it silently broke that mechanism's own test before this scoping was added).
    /// Narrow, accepted risk: two genuinely different real Plaid transactions on the same
    /// account/amount within the window would collide - same tradeoff already accepted by
    /// ExistsForAccountDateAmountAsync above.
    /// </summary>
    public async Task<BankTransaction?> FindPendingMatchAsync(
        ExpenseDbContext context, int accountId, decimal amount, DateOnly transactionDate, int windowDays = 10) =>
        await context.BankTransactions.FirstOrDefaultAsync(t =>
            t.AccountId == accountId && t.Amount == amount && t.PostedDate == null && t.ImportSource == "Plaid"
            && t.TransactionDate >= transactionDate.AddDays(-windowDays) && t.TransactionDate <= transactionDate.AddDays(windowDays));

    /// <summary>
    /// The mirror image of FindPendingMatchAsync: true if an incoming *pending*
    /// transaction actually represents a real charge that's already fully posted in our
    /// system. Plaid can re-report an already-resolved transaction as pending again under
    /// a brand new transaction_id, with no pending_transaction_id link back to anything -
    /// confirmed for real on 2026-07-29 (4 real transactions duplicated this way). Nothing
    /// else catches this: ExistsForAccountDateAmountAsync only ever runs for posted
    /// incoming transactions (it needs a postedDate to compare with), and
    /// FindPendingMatchAsync only matches posted incoming transactions against pending
    /// existing rows, never the reverse. Not scoped by ImportSource on the existing side -
    /// the already-posted row could equally have come from SimpleFin, and there's no
    /// manually-entered-placeholder risk here (a placeholder is never posted, by
    /// definition, until ManualChargeMatchingService resolves it). Same accepted narrow
    /// risk and date window as FindPendingMatchAsync.
    /// </summary>
    public async Task<bool> ExistsAlreadyPostedAsync(
        ExpenseDbContext context, int accountId, decimal amount, DateOnly transactionDate, int windowDays = 10) =>
        await context.BankTransactions.AnyAsync(t =>
            t.AccountId == accountId && t.Amount == amount && t.PostedDate != null
            && t.TransactionDate >= transactionDate.AddDays(-windowDays) && t.TransactionDate <= transactionDate.AddDays(windowDays));
}
