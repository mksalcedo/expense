using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Ingestion.ManualCharges;

public class ManualChargeMatchingServiceTests : DatabaseTestBase
{
    private readonly ManualChargeMatchingService _sut = new();

    private async Task<Account> CreateAccountAsync() =>
        (await Context.Accounts.AddAsync(new Account { Name = "Amex", Type = AccountType.ActiveSpending })).Entity;

    [Fact]
    public async Task FindExistingMatchAsync_FindsAPostedTransaction_WithinTheWindow()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 18), PostedDate = new DateOnly(2026, 7, 18),
            Description = "INGLES MARKETS #474 NORCROSS GA", Amount = -171.95m, ImportSource = "SimpleFin", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var match = await _sut.FindExistingMatchAsync(Context, account.Id, new DateOnly(2026, 7, 20), -171.95m);

        Assert.NotNull(match);
        Assert.Equal("INGLES MARKETS #474 NORCROSS GA", match.Description);
    }

    [Fact]
    public async Task FindExistingMatchAsync_FindsAnOpenManualPlaceholder_ByTransactionDate()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = null,
            Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var match = await _sut.FindExistingMatchAsync(Context, account.Id, new DateOnly(2026, 7, 20), -131.65m);

        Assert.NotNull(match);
    }

    [Fact]
    public async Task FindExistingMatchAsync_NoMatch_WhenAmountDiffers()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 18), PostedDate = new DateOnly(2026, 7, 18),
            Description = "INGLES MARKETS #474 NORCROSS GA", Amount = -171.95m, ImportSource = "SimpleFin", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var match = await _sut.FindExistingMatchAsync(Context, account.Id, new DateOnly(2026, 7, 18), -99.00m);

        Assert.Null(match);
    }

    [Fact]
    public async Task FindExistingMatchAsync_NoMatch_WhenOutsideTheWindow()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), PostedDate = new DateOnly(2026, 7, 1),
            Description = "INGLES MARKETS #474 NORCROSS GA", Amount = -171.95m, ImportSource = "SimpleFin", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var match = await _sut.FindExistingMatchAsync(Context, account.Id, new DateOnly(2026, 7, 20), -171.95m);

        Assert.Null(match);
    }

    [Fact]
    public async Task FindExistingMatchAsync_NoMatch_WhenOnADifferentAccount()
    {
        var amex = await CreateAccountAsync();
        var discover = new Account { Name = "Discover", Type = AccountType.Debt };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = discover.Id, TransactionDate = new DateOnly(2026, 7, 18), PostedDate = new DateOnly(2026, 7, 18),
            Description = "INGLES MARKETS #474 NORCROSS GA", Amount = -171.95m, ImportSource = "SimpleFin", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var match = await _sut.FindExistingMatchAsync(Context, amex.Id, new DateOnly(2026, 7, 18), -171.95m);

        Assert.Null(match);
    }

    [Fact]
    public async Task FindOpenPlaceholderMatchAsync_FindsAnUnpostedManualEntry_WithinTheWindow()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        var placeholder = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = null,
            Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(placeholder);
        await Context.SaveChangesAsync();

        // The real transaction eventually posts a couple of days later, with a fuller description.
        var match = await _sut.FindOpenPlaceholderMatchAsync(Context, account.Id, new DateOnly(2026, 7, 22), -131.65m);

        Assert.NotNull(match);
        Assert.Equal(placeholder.Id, match.Id);
    }

    [Fact]
    public async Task FindOpenPlaceholderMatchAsync_IgnoresAlreadyPostedTransactions()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = new DateOnly(2026, 7, 20),
            Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "SimpleFin", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var match = await _sut.FindOpenPlaceholderMatchAsync(Context, account.Id, new DateOnly(2026, 7, 22), -131.65m);

        Assert.Null(match);
    }

    [Fact]
    public async Task ReconcilePlaceholdersAsync_RemovesTheMatchingPlaceholder_AndReportsHowMany()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        var placeholder = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = null,
            Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(placeholder);
        await Context.SaveChangesAsync();

        var realTransaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 22), PostedDate = new DateOnly(2026, 7, 22),
            Description = "MORGAN COMPOUNDING PHARMACY", Amount = -131.65m, ImportSource = "SimpleFin", CreatedAt = DateTimeOffset.UtcNow
        };

        var removedCount = await _sut.ReconcilePlaceholdersAsync(Context, [realTransaction]);

        Assert.Equal(1, removedCount);
        Assert.False(await Context.BankTransactions.AnyAsync(t => t.Id == placeholder.Id));
    }

    [Fact]
    public async Task ReconcilePlaceholdersAsync_LeavesNonMatchingTransactionsAlone()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        var placeholder = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = null,
            Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(placeholder);
        await Context.SaveChangesAsync();

        var unrelatedTransaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 22), PostedDate = new DateOnly(2026, 7, 22),
            Description = "PUBLIX", Amount = -40.00m, ImportSource = "SimpleFin", CreatedAt = DateTimeOffset.UtcNow
        };

        var removedCount = await _sut.ReconcilePlaceholdersAsync(Context, [unrelatedTransaction]);

        Assert.Equal(0, removedCount);
        Assert.True(await Context.BankTransactions.AnyAsync(t => t.Id == placeholder.Id));
    }

    [Fact]
    public async Task DeletePlaceholderAsync_RemovesAnOpenManualScreenshotPlaceholder()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        var placeholder = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = null,
            Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(placeholder);
        await Context.SaveChangesAsync();

        await _sut.DeletePlaceholderAsync(Context, placeholder.Id);

        Assert.False(await Context.BankTransactions.AnyAsync(t => t.Id == placeholder.Id));
    }

    [Fact]
    public async Task DeletePlaceholderAsync_LeavesAnAlreadyPostedTransactionAlone()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = new DateOnly(2026, 7, 20),
            Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(transaction);
        await Context.SaveChangesAsync();

        await _sut.DeletePlaceholderAsync(Context, transaction.Id);

        Assert.True(await Context.BankTransactions.AnyAsync(t => t.Id == transaction.Id));
    }

    [Fact]
    public async Task DeletePlaceholderAsync_LeavesANonManualScreenshotTransactionAlone()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = null,
            Description = "SOME OTHER PENDING SOURCE", Amount = -131.65m, ImportSource = "SomeOtherSource", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(transaction);
        await Context.SaveChangesAsync();

        await _sut.DeletePlaceholderAsync(Context, transaction.Id);

        Assert.True(await Context.BankTransactions.AnyAsync(t => t.Id == transaction.Id));
    }

    [Fact]
    public async Task FindOpenPlaceholderMatchAsync_IgnoresNonManualScreenshotSources()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = null,
            Description = "SOME OTHER PENDING SOURCE", Amount = -131.65m, ImportSource = "SomeOtherSource", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var match = await _sut.FindOpenPlaceholderMatchAsync(Context, account.Id, new DateOnly(2026, 7, 22), -131.65m);

        Assert.Null(match);
    }
}
