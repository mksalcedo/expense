using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Tests.TestSupport;

namespace Expense.Domain.Tests.Services;

public class DedupServiceExistsTests : DatabaseTestBase
{
    private readonly DedupService _sut = new();

    private async Task<Account> CreateAccountAsync()
    {
        var account = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task ExistsAsync_MatchesOnExternalId_WhenPresent()
    {
        var account = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "INGLES", Amount = -50m, ImportSource = "SimpleFin",
            ExternalId = "wf-existing-123", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var exists = await _sut.ExistsAsync(Context, account.Id, externalId: "wf-existing-123", fingerprint: null);

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_MatchesOnFingerprint_WhenNoExternalId()
    {
        var account = await CreateAccountAsync();
        var fingerprint = DedupService.GenerateFingerprint(account.Id, new DateOnly(2026, 7, 13), -50m, "INGLES");

        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "INGLES", Amount = -50m, ImportSource = "SimpleFin",
            DedupFingerprint = fingerprint, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var exists = await _sut.ExistsAsync(Context, account.Id, externalId: null, fingerprint: fingerprint);

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_ForGenuinelyNewTransaction()
    {
        var account = await CreateAccountAsync();

        var exists = await _sut.ExistsAsync(Context, account.Id, externalId: "brand-new-id", fingerprint: null);

        Assert.False(exists);
    }
}
