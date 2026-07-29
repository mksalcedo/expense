using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.SimpleFin;

/// <summary>
/// Ties SimpleFinClient + DedupService + CategorizationService together for a real
/// import run. Active-spending accounts (Amex, checking) get both a balance snapshot
/// and their transactions imported/categorized; debt accounts only ever get a balance
/// snapshot - any transaction-level data SimpleFin returns for them is discarded,
/// since debt accounts were never meant to feed the Spending Tracker.
/// </summary>
public class SimpleFinImportService(SimpleFinClient client, DedupService dedup, CategorizationService categorization)
{
    public async Task<ImportSummary> ImportAsync(
        ExpenseDbContext context,
        IReadOnlyDictionary<string, int> accountMap,
        DateTimeOffset startDate,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetAccountsAsync(startDate, cancellationToken);
        var summary = new ImportSummary();

        foreach (var simpleFinAccount in response.Accounts)
        {
            if (!accountMap.TryGetValue(simpleFinAccount.Id, out var localAccountId))
            {
                summary.UnmappedAccounts.Add(simpleFinAccount.Id);
                continue;
            }

            var localAccount = await context.Accounts.SingleAsync(a => a.Id == localAccountId, cancellationToken);
            var balanceTimestamp = DateTimeOffset.FromUnixTimeSeconds(simpleFinAccount.BalanceDateUnix);
            var balanceDate = DateOnly.FromDateTime(balanceTimestamp.UtcDateTime);

            if (localAccount.Type == AccountType.Debt)
            {
                context.DebtBalanceSnapshots.Add(new DebtBalanceSnapshot
                {
                    AccountId = localAccount.Id,
                    AsOfDate = balanceDate,
                    Balance = simpleFinAccount.Balance
                });
                summary.BalanceSnapshotsAdded++;
                continue; // transactions deliberately discarded for debt accounts
            }

            if (localAccount.Type == AccountType.Checking)
            {
                context.CheckingBalanceSnapshots.Add(new CheckingBalanceSnapshot
                {
                    AsOfDate = balanceDate,
                    AsOfTimestamp = balanceTimestamp,
                    Balance = simpleFinAccount.Balance
                });
                summary.BalanceSnapshotsAdded++;
            }

            await ImportTransactionsAsync(context, localAccount, simpleFinAccount.Transactions, summary, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        return summary;
    }

    private async Task ImportTransactionsAsync(
        ExpenseDbContext context,
        Account account,
        List<SimpleFinTransaction> transactions,
        ImportSummary summary,
        CancellationToken cancellationToken)
    {
        var occurrenceCounts = new Dictionary<string, int>();

        foreach (var txn in transactions)
        {
            // SimpleFin only ever reports already-posted transactions in this array
            var postedDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(txn.PostedUnix).UtcDateTime);

            string? fingerprint = null;
            if (string.IsNullOrEmpty(txn.Id))
            {
                var baseFingerprint = DedupService.GenerateFingerprint(account.Id, postedDate, txn.Amount, txn.Description);
                occurrenceCounts.TryGetValue(baseFingerprint, out var occurrence);
                occurrenceCounts[baseFingerprint] = occurrence + 1;
                fingerprint = occurrence == 0
                    ? baseFingerprint
                    : DedupService.GenerateFingerprint(account.Id, postedDate, txn.Amount, txn.Description, occurrence);
            }

            // A Plaid-pending row later posting through SimpleFin instead of Plaid - the
            // cross-source check below only catches an already-posted match (it compares
            // by PostedDate, which a pending row never has), so this must be checked first
            // and merged into, not treated as either a fresh transaction or a duplicate to
            // skip. Confirmed against 4 real duplicate transactions on 2026-07-29.
            var pendingMatch = await dedup.FindPendingMatchAsync(context, account.Id, txn.Amount, postedDate);
            if (pendingMatch is not null)
            {
                pendingMatch.PostedDate = postedDate;
                pendingMatch.Description = txn.Description;
                pendingMatch.Amount = txn.Amount;
                pendingMatch.ExternalId = string.IsNullOrEmpty(txn.Id) ? null : txn.Id;
                summary.PendingTransactionsUpdated++;
                continue;
            }

            var isDuplicate = await dedup.ExistsAsync(context, account.Id, txn.Id, fingerprint);
            if (!isDuplicate)
            {
                // Cross-source check - catches this same real transaction already having
                // been imported via Plaid (e.g. a stale-SimpleFin backfill) under a
                // different id/description. Mirrors the equivalent check already applied
                // on the Plaid importer's own path - this was the missing, one-directional
                // half of it (see PlaidTransactionImportService.ImportAsync).
                isDuplicate = await dedup.ExistsForAccountDateAmountAsync(context, account.Id, postedDate, txn.Amount);
            }

            if (isDuplicate)
            {
                summary.DuplicatesSkipped++;
                continue;
            }

            var isAmazon = txn.Description.Contains("AMAZON", StringComparison.OrdinalIgnoreCase);
            var bankTransaction = new BankTransaction
            {
                AccountId = account.Id,
                TransactionDate = postedDate,
                PostedDate = postedDate,
                Description = txn.Description,
                Amount = txn.Amount,
                ExternalId = string.IsNullOrEmpty(txn.Id) ? null : txn.Id,
                ImportSource = "SimpleFin",
                DedupFingerprint = fingerprint,
                IsAmazonMerchant = isAmazon,
                CreatedAt = DateTimeOffset.UtcNow
            };

            if (!isAmazon)
            {
                await categorization.ApplyMerchantRuleAsync(context, bankTransaction);
            }

            context.BankTransactions.Add(bankTransaction);
            summary.TransactionsAdded++;
            summary.NewTransactions.Add(bankTransaction);
        }
    }
}
