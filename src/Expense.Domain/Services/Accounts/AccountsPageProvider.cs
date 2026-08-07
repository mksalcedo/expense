using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Accounts;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in AccountManagementService.</summary>
public class AccountsPageProvider(IDbContextFactory<ExpenseDbContext> contextFactory, AccountManagementService accounts) : IAccountsPageProvider
{
    public async Task<AccountsPageData> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Small, hand-entered table (no automatic sync writes to it currently) - loading it
        // whole and grouping in memory avoids relying on GroupBy+First() translating cleanly.
        var latestByAccount = (await context.DebtBalanceSnapshots.ToListAsync(cancellationToken))
            .GroupBy(s => s.AccountId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.AsOfDate).First());

        var rows = await context.Accounts
            .OrderBy(a => a.Name)
            .Select(a => new AccountRow
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                IsActive = a.IsActive,
                MinPayment = a.MinPayment,
                ExtraPayment = a.ExtraPayment,
                PaymentDueDay = a.PaymentDueDay,
                StatementCloseDay = a.StatementCloseDay,
                Apr = a.Apr,
                PaymentStartDate = a.PaymentStartDate
            })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (!latestByAccount.TryGetValue(row.Id, out var snapshot)) continue;
            row.LatestBalance = snapshot.Balance;
            row.LatestBalanceAsOfDate = snapshot.AsOfDate;
        }

        return new AccountsPageData { Accounts = rows };
    }

    public async Task<int> CreateAccountAsync(
        string name, AccountType type, decimal? minPayment, decimal? extraPayment,
        int? paymentDueDay, int? statementCloseDay, decimal? apr, DateOnly? paymentStartDate = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await accounts.CreateAccountAsync(context, name, type, minPayment, extraPayment, paymentDueDay, statementCloseDay, apr: apr, paymentStartDate: paymentStartDate);
        return account.Id;
    }

    public async Task UpdateAccountAsync(
        int accountId, string name, decimal? minPayment, decimal? extraPayment,
        int? paymentDueDay, int? statementCloseDay, decimal? apr, DateOnly? paymentStartDate = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await accounts.UpdateAccountAsync(context, accountId, name, minPayment, extraPayment, paymentDueDay, statementCloseDay, apr, paymentStartDate);
    }

    public async Task UpdateBalanceAsync(int accountId, DateOnly asOfDate, decimal balance, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await accounts.AddOrUpdateBalanceSnapshotAsync(context, accountId, asOfDate, balance);
    }

    public async Task DeactivateAccountAsync(int accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await accounts.DeactivateAccountAsync(context, accountId);
    }

    public async Task ReactivateAccountAsync(int accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await accounts.ReactivateAccountAsync(context, accountId);
    }
}
