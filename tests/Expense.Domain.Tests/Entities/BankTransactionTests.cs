using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class BankTransactionTests : DatabaseTestBase
{
    private async Task<Account> CreateAccountAsync(string name, AccountType type)
    {
        var account = new Account { Name = name, Type = type };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task Transaction_SavedAndReloaded_RoundTripsCorrectly()
    {
        var checking = await CreateAccountAsync("Wells Fargo Checking", AccountType.Checking);

        var txn = new BankTransaction
        {
            AccountId = checking.Id,
            TransactionDate = new DateOnly(2026, 7, 13),
            PostedDate = new DateOnly(2026, 7, 13),
            Description = "AMEX EPAYMENT ACH PMT",
            Merchant = "American Express",
            Amount = -2334.15m,
            ExternalId = "wf-12345",
            ImportSource = "SimpleFin",
            IsAmazonMerchant = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(txn);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.BankTransactions.SingleAsync(t => t.Id == txn.Id);

        Assert.Equal(-2334.15m, reloaded.Amount);
        Assert.Equal("wf-12345", reloaded.ExternalId);
        Assert.Equal(new DateOnly(2026, 7, 13), reloaded.PostedDate);
    }

    [Fact]
    public async Task PendingTransaction_HasNoPostedDate()
    {
        var amex = await CreateAccountAsync("Amex", AccountType.ActiveSpending);

        var txn = new BankTransaction
        {
            AccountId = amex.Id,
            TransactionDate = new DateOnly(2026, 7, 14),
            PostedDate = null,
            Description = "AMAZON MARKETPLACE",
            Amount = -30.73m,
            ImportSource = "SimpleFin",
            IsAmazonMerchant = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(txn);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.BankTransactions.SingleAsync(t => t.Id == txn.Id);

        Assert.Null(reloaded.PostedDate);
    }

    [Fact]
    public async Task AmazonMerchantTransaction_NeverHasACategory_EvenWhenSaved()
    {
        var amex = await CreateAccountAsync("Amex", AccountType.ActiveSpending);

        var txn = new BankTransaction
        {
            AccountId = amex.Id,
            TransactionDate = new DateOnly(2026, 7, 14),
            Description = "AMAZON MARKETPLACE",
            Amount = -30.73m,
            ImportSource = "SimpleFin",
            IsAmazonMerchant = true,
            CategoryId = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(txn);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.BankTransactions.SingleAsync(t => t.Id == txn.Id);

        Assert.True(reloaded.IsAmazonMerchant);
        Assert.Null(reloaded.CategoryId);
    }

    [Fact]
    public async Task PendingCategorizationQuery_FindsOnlyUncategorizedRows()
    {
        var checking = await CreateAccountAsync("Wells Fargo Checking", AccountType.Checking);
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        Context.BankTransactions.AddRange(
            new BankTransaction
            {
                AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 12),
                Description = "INGLES", Amount = -50m, ImportSource = "SimpleFin",
                CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            new BankTransaction
            {
                AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 13),
                Description = "SOME NEW MERCHANT", Amount = -20m, ImportSource = "SimpleFin",
                CategoryId = null, CreatedAt = DateTimeOffset.UtcNow
            }
        );
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var pending = await reloadContext.BankTransactions
            .Where(t => t.CategoryId == null)
            .ToListAsync();

        Assert.Single(pending);
        Assert.Equal("SOME NEW MERCHANT", pending[0].Description);
    }

    [Fact]
    public async Task DedupFingerprint_MustBeUniqueWhenPresent()
    {
        var checking = await CreateAccountAsync("Wells Fargo Checking", AccountType.Checking);
        const string fingerprint = "checking|2026-07-13|-50.00|INGLES";

        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "INGLES", Amount = -50m, ImportSource = "SimpleFin",
            DedupFingerprint = fingerprint, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "INGLES", Amount = -50m, ImportSource = "SimpleFin",
            DedupFingerprint = fingerprint, CreatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }
}
