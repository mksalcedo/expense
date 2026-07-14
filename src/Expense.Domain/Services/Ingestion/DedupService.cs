using Expense.Domain.Data;
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
}
