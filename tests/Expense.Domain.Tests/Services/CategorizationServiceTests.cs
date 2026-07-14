using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services;

public class CategorizationServiceTests : DatabaseTestBase
{
    private readonly CategorizationService _sut = new();

    private async Task<Account> CreateAccountAsync() =>
        (await Context.Accounts.AddAsync(new Account { Name = "Amex", Type = AccountType.ActiveSpending })).Entity;

    [Fact]
    public async Task Transaction_MatchingAMerchantRule_GetsCategorizedOnImport()
    {
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries", IsBudgeted = true };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "%INGLES%", CategoryId = groceries.Id });
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "INGLES #123 NORCROSS GA", Amount = -50m, ImportSource = "SimpleFin",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _sut.ApplyMerchantRuleAsync(Context, transaction);

        Assert.Equal(groceries.Id, transaction.CategoryId);
    }

    [Fact]
    public async Task Transaction_WithNoMatchingRule_StaysPendingCategorization()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "SOME BRAND NEW MERCHANT", Amount = -20m, ImportSource = "SimpleFin",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _sut.ApplyMerchantRuleAsync(Context, transaction);

        Assert.Null(transaction.CategoryId);
    }

    [Fact]
    public async Task AmazonMerchantTransaction_NeverGetsCategorized_EvenIfAPatternWouldMatch()
    {
        var account = await CreateAccountAsync();
        var misc = new Category { Name = "Off-Budget/Misc", IsBudgeted = false };
        Context.Categories.Add(misc);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "%AMAZON%", CategoryId = misc.Id });
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "AMAZON MARKETPLACE", Amount = -30.73m, ImportSource = "SimpleFin",
            IsAmazonMerchant = true, CreatedAt = DateTimeOffset.UtcNow
        };

        await _sut.ApplyMerchantRuleAsync(Context, transaction);

        Assert.Null(transaction.CategoryId);
    }

    [Fact]
    public async Task MerchantRuleMatching_IsCaseInsensitive()
    {
        var account = await CreateAccountAsync();
        var restaurants = new Category { Name = "Restaurants", IsBudgeted = true };
        Context.Categories.Add(restaurants);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "%chipotle%", CategoryId = restaurants.Id });
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "CHIPOTLE 1652 NORCROSS GA", Amount = -25.39m, ImportSource = "SimpleFin",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _sut.ApplyMerchantRuleAsync(Context, transaction);

        Assert.Equal(restaurants.Id, transaction.CategoryId);
    }
}
