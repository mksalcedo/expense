using Expense.Domain.Entities;

namespace Expense.Domain.Services.Accounts;

/// <summary>Thin abstraction over AccountManagementService so UI components can be tested against a fake result.</summary>
public interface IAccountsPageProvider
{
    Task<AccountsPageData> GetAccountsAsync(CancellationToken cancellationToken = default);

    Task CreateAccountAsync(
        string name, AccountType type, decimal? minPayment, decimal? extraPayment,
        int? paymentDueDay, int? statementCloseDay, CancellationToken cancellationToken = default);

    Task UpdateAccountAsync(
        int accountId, string name, decimal? minPayment, decimal? extraPayment,
        int? paymentDueDay, int? statementCloseDay, CancellationToken cancellationToken = default);

    Task DeactivateAccountAsync(int accountId, CancellationToken cancellationToken = default);

    Task ReactivateAccountAsync(int accountId, CancellationToken cancellationToken = default);
}
