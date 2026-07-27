using System.Text.Json;
using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
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
        CancellationToken cancellationToken = default)
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
                continue;
            }

            // Plaid reports positive = money out, negative = money in - the opposite of
            // this app's convention (a deposit is positive) - confirmed directly against
            // real data (a real payroll deposit came back as -4492.86).
            var amount = -txn.Amount;
            var postedDate = txn.Pending ? (DateOnly?)null : txn.Date;

            var isDuplicate = await dedup.ExistsAsync(context, localAccountId, externalId: txn.TransactionId, fingerprint: null);
            if (!isDuplicate && postedDate is not null)
            {
                // Cross-source check - catches this same real transaction already having
                // been imported via SimpleFin under a different id/description.
                isDuplicate = await dedup.ExistsForAccountDateAmountAsync(context, localAccountId, postedDate.Value, amount);
            }

            if (isDuplicate)
            {
                summary.DuplicatesSkipped++;
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
        }

        await context.SaveChangesAsync(cancellationToken);
        return summary;
    }

    private static PlaidTransactionsPayload ParsePayload(string rawCliOutput)
    {
        foreach (var line in rawCliOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("transactions", out _))
            {
                return JsonSerializer.Deserialize<PlaidTransactionsPayload>(line)
                    ?? throw new InvalidOperationException("Plaid CLI transactions payload line failed to deserialize.");
            }
        }

        throw new InvalidOperationException("No line in the Plaid CLI output contained a transactions array.");
    }
}
