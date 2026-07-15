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
                StatementCloseDay = a.StatementCloseDay
            })
            .ToListAsync(cancellationToken);

        return new AccountsPageData { Accounts = rows };
    }

    public async Task CreateAccountAsync(
        string name, AccountType type, decimal? minPayment, decimal? extraPayment,
        int? paymentDueDay, int? statementCloseDay, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await accounts.CreateAccountAsync(context, name, type, minPayment, extraPayment, paymentDueDay, statementCloseDay);
    }

    public async Task UpdateAccountAsync(
        int accountId, string name, decimal? minPayment, decimal? extraPayment,
        int? paymentDueDay, int? statementCloseDay, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await accounts.UpdateAccountAsync(context, accountId, name, minPayment, extraPayment, paymentDueDay, statementCloseDay);
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
