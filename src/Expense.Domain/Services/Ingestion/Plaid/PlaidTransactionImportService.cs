using System.Text.Json;
using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Services.Ingestion.SimpleFin;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.Plaid;

/// <summary>
/// Imports a manually-produced Plaid CLI transactions export (see
/// docs/plaid-import-utility-plan.md) - a small, on-demand backstop for accounts where
/// SimpleFin's own connection has gone stale, not a scheduled/automatic source. The CLI's
/// own stdout is not single JSON - it's a "diagnostic" progress line followed by the real
/// payload line - this scans for whichever line actually has a "transactions" property.
/// </summary>
public class PlaidTransactionImportService(DedupService dedup, CategorizationService categorization)
{
    public async Task<ImportSummary> ImportAsync(
        ExpenseDbContext context, string rawCliOutput, IReadOnlyDictionary<string, int> accountMap, DateTimeOffset asOfTimestamp,
        Action<SyncProgressLine>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var summary = new ImportSummary();
        var payload = ParsePayload(rawCliOutput);

        // Only Checking accounts get a balance snapshot here, same as SimpleFinImportService -
        // Debt/ActiveSpending accounts have their own separate balance-tracking mechanisms
        // (or none yet) that this first slice doesn't touch.
        foreach (var accountInfo in payload.Accounts)
        {
            if (!accountMap.TryGetValue(accountInfo.AccountId, out var localAccountId) || accountInfo.Balances.Available is null)
            {
                continue;
            }

            var localAccount = await context.Accounts.SingleAsync(a => a.Id == localAccountId, cancellationToken);
            if (localAccount.Type != AccountType.Checking)
            {
                continue;
            }

            context.CheckingBalanceSnapshots.Add(new CheckingBalanceSnapshot
            {
                AsOfDate = DateOnly.FromDateTime(asOfTimestamp.UtcDateTime),
                AsOfTimestamp = asOfTimestamp,
                Balance = accountInfo.Balances.Available.Value
            });
            summary.BalanceSnapshotsAdded++;
        }

        foreach (var txn in payload.Transactions)
        {
            if (!accountMap.TryGetValue(txn.AccountId, out var localAccountId))
            {
                summary.UnmappedAccounts.Add(txn.AccountId);
                onProgress?.Invoke(new SyncProgressLine(
                    $"{txn.Name} {-txn.Amount:N2} ({txn.Date:MM/dd/yyyy}) - unmapped account {txn.AccountId}, skipped", IsError: true));
                continue;
            }

            // Plaid reports positive = money out, negative = money in - the opposite of
            // this app's convention (a deposit is positive) - confirmed directly against
            // real data (a real payroll deposit came back as -4492.86).
            var amount = -txn.Amount;
            var postedDate = txn.Pending ? (DateOnly?)null : txn.Date;

            // Plaid never updates a pending transaction in place when it posts - it issues
            // a brand new transaction_id and links back to the original via
            // pending_transaction_id, when it supplies that field at all (it's only
            // populated "when available" - confirmed missing for two real transactions on
            // 2026-07-29, which is why DedupService.FindPendingMatchAsync exists as a
            // fallback). Merge into the existing row (preserving whatever category it
            // already has) instead of inserting a second one; ExternalId-based dedup can't
            // catch this since the ids legitimately differ, and the posted-date cross-check
            // can't either since the existing row's PostedDate is still null.
            if (!txn.Pending)
            {
                BankTransaction? pendingRow = null;
                if (txn.PendingTransactionId is not null)
                {
                    pendingRow = await context.BankTransactions.SingleOrDefaultAsync(
                        t => t.AccountId == localAccountId && t.ExternalId == txn.PendingTransactionId, cancellationToken);
                }
                pendingRow ??= await dedup.FindPendingMatchAsync(context, localAccountId, amount, txn.Date);

                if (pendingRow is not null)
                {
                    pendingRow.PostedDate = postedDate;
                    pendingRow.Description = txn.Name;
                    pendingRow.Merchant = txn.MerchantName;
                    pendingRow.Amount = amount;
                    pendingRow.ExternalId = txn.TransactionId;
                    summary.PendingTransactionsUpdated++;
                    onProgress?.Invoke(new SyncProgressLine(
                        $"{txn.Name} {amount:N2} ({txn.Date:MM/dd/yyyy}) - merged into pending row (id {pendingRow.Id})"));
                    continue;
                }
            }

            var isDuplicate = await dedup.ExistsAsync(context, localAccountId, externalId: txn.TransactionId, fingerprint: null);
            if (!isDuplicate && postedDate is not null)
            {
                // Cross-source check - catches this same real transaction already having
                // been imported via SimpleFin under a different id/description.
                isDuplicate = await dedup.ExistsForAccountDateAmountAsync(context, localAccountId, postedDate.Value, amount);
            }

            if (!isDuplicate && txn.Pending)
            {
                // This pending transaction might actually represent a real charge we've
                // already fully resolved (posted) - Plaid can re-report an already-settled
                // transaction as pending again under a brand new id, confirmed for real on
                // 2026-07-29 (4 real transactions). Skip it rather than creating a
                // redundant pending shadow of something already resolved.
                isDuplicate = await dedup.ExistsAlreadyPostedAsync(context, localAccountId, amount, txn.Date);
            }

            if (isDuplicate)
            {
                summary.DuplicatesSkipped++;
                onProgress?.Invoke(new SyncProgressLine($"{txn.Name} {amount:N2} ({txn.Date:MM/dd/yyyy}) - duplicate, already imported"));
                continue;
            }

            var bankTransaction = new BankTransaction
            {
                AccountId = localAccountId,
                TransactionDate = txn.Date,
                PostedDate = postedDate,
                Description = txn.Name,
                Merchant = txn.MerchantName,
                Amount = amount,
                ExternalId = txn.TransactionId,
                ImportSource = "Plaid",
                CreatedAt = DateTimeOffset.UtcNow
            };

            await categorization.ApplyMerchantRuleAsync(context, bankTransaction);

            context.BankTransactions.Add(bankTransaction);
            summary.TransactionsAdded++;
            summary.NewTransactions.Add(bankTransaction);
            onProgress?.Invoke(new SyncProgressLine($"{txn.Name} {amount:N2} ({txn.Date:MM/dd/yyyy}) - added"));
        }

        onProgress?.Invoke(new SyncProgressLine(
            $"Done - transactions added: {summary.TransactionsAdded}, duplicates skipped: {summary.DuplicatesSkipped}, " +
            $"pending transactions updated: {summary.PendingTransactionsUpdated}, balance snapshots added: {summary.BalanceSnapshotsAdded}"));

        await context.SaveChangesAsync(cancellationToken);
        return summary;
    }

    private static PlaidTransactionsPayload ParsePayload(string rawCliOutput)
    {
        foreach (var line in rawCliOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);

            // --all output (more than one linked item) has no top-level "accounts"/
            // "transactions" at all - each item nests its own inside a top-level "items"
            // array instead. Flatten into the same shape single-item output already uses,
            // so the rest of ImportAsync never needs to know which one it got.
            if (doc.RootElement.TryGetProperty("items", out _))
            {
                var envelope = JsonSerializer.Deserialize<PlaidAllItemsPayload>(line)
                    ?? throw new InvalidOperationException("Plaid CLI --all transactions payload line failed to deserialize.");
                return new PlaidTransactionsPayload
                {
                    Accounts = envelope.Items.SelectMany(i => i.Accounts).ToList(),
                    Transactions = envelope.Items.SelectMany(i => i.Transactions).ToList()
                };
            }

            if (doc.RootElement.TryGetProperty("transactions", out _))
            {
                return JsonSerializer.Deserialize<PlaidTransactionsPayload>(line)
                    ?? throw new InvalidOperationException("Plaid CLI transactions payload line failed to deserialize.");
            }
        }

        throw new InvalidOperationException("No line in the Plaid CLI output contained a transactions array.");
    }
}
