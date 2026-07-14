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
            if (!accountMap.TryGetValue(simpleFinAccount.Name, out var localAccountId))
            {
                summary.UnmappedAccounts.Add(simpleFinAccount.Name);
                continue;
            }

            var localAccount = await context.Accounts.SingleAsync(a => a.Id == localAccountId, cancellationToken);
            var balanceDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(simpleFinAccount.BalanceDateUnix).UtcDateTime);

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

            if (await dedup.ExistsAsync(context, account.Id, txn.Id, fingerprint))
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
        }
    }
}
