using Expense.Domain.Entities;

namespace Expense.Domain.Services.Accounts;

/// <summary>Thin abstraction over AccountManagementService so UI components can be tested against a fake result.</summary>
public interface IAccountsPageProvider
{
    Task<AccountsPageData> GetAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the new account's id, so the caller can keep working with it (e.g. switch straight into editing it) without a second round trip.</summary>
    Task<int> CreateAccountAsync(
        string name, AccountType type, decimal? minPayment, decimal? extraPayment,
        int? paymentDueDay, int? statementCloseDay, decimal? apr, CancellationToken cancellationToken = default);

    Task UpdateAccountAsync(
        int accountId, string name, decimal? minPayment, decimal? extraPayment,
        int? paymentDueDay, int? statementCloseDay, decimal? apr, CancellationToken cancellationToken = default);

    Task DeactivateAccountAsync(int accountId, CancellationToken cancellationToken = default);

    Task ReactivateAccountAsync(int accountId, CancellationToken cancellationToken = default);

    /// <summary>Records a balance as of a specific date - upserts by (account, date), see AccountManagementService.AddOrUpdateBalanceSnapshotAsync.</summary>
    Task UpdateBalanceAsync(int accountId, DateOnly asOfDate, decimal balance, CancellationToken cancellationToken = default);
}
