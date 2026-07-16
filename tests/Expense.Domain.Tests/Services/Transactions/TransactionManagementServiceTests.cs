using Expense.Domain.Entities;
using Expense.Domain.Services.Transactions;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Transactions;

public class TransactionManagementServiceTests : DatabaseTestBase
{
    private readonly TransactionManagementService _sut = new();

    private async Task<Account> CreateAccountAsync() =>
        (await Context.Accounts.AddAsync(new Account { Name = "Amex", Type = AccountType.ActiveSpending })).Entity;

    [Fact]
    public async Task GetTransactionsAsync_ReturnsAllNonAmazonTransactions_NewestFirst_WithCategoryNames()
    {
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 5), Description = "UNKNOWN MERCHANT", Amount = -10m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 3), Description = "AMAZON MARKETPLACE", Amount = -20m, ImportSource = "Test", IsAmazonMerchant = true, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var rows = await _sut.GetTransactionsAsync(Context, searchText: null);

        Assert.Equal(2, rows.Count); // Amazon-merchant row excluded
        Assert.Equal("UNKNOWN MERCHANT", rows[0].Description); // newest first
        Assert.Null(rows[0].CategoryId);
        Assert.Null(rows[0].CategoryName);
        Assert.Equal("PUBLIX", rows[1].Description);
        Assert.Equal(groceries.Id, rows[1].CategoryId);
        Assert.Equal("Groceries", rows[1].CategoryName);
    }

    [Fact]
    public async Task GetTransactionsAsync_WithSearchText_FiltersByDescription_CaseInsensitive()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "TRUIST MORTG PAYMENT", Amount = -2681.22m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 2), Description = "PUBLIX NORCROSS GA", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var rows = await _sut.GetTransactionsAsync(Context, searchText: "truist");

        var row = Assert.Single(rows);
        Assert.Equal("TRUIST MORTG PAYMENT", row.Description);
    }

    [Fact]
    public async Task UpdateCategoryAsync_ChangesAnAlreadyCategorizedTransactionToADifferentCategory()
    {
        var account = await CreateAccountAsync();
        var wrong = new Category { Name = "Restaurants" };
        var correct = new Category { Name = "Groceries" };
        Context.Categories.AddRange(wrong, correct);
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX",
            Amount = -40m, ImportSource = "Test", CategoryId = wrong.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(transaction);
        await Context.SaveChangesAsync();

        await _sut.UpdateCategoryAsync(Context, transaction.Id, correct.Id);

        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(correct.Id, reloaded.CategoryId);
    }

    [Fact]
    public async Task UpdateCategoryAsync_CanSetBackToUncategorized()
    {
        var account = await CreateAccountAsync();
        var category = new Category { Name = "Groceries" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX",
            Amount = -40m, ImportSource = "Test", CategoryId = category.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(transaction);
        await Context.SaveChangesAsync();

        await _sut.UpdateCategoryAsync(Context, transaction.Id, null);

        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(reloaded.CategoryId);
    }
}
